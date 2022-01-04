using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Matlabs.OwlRacer.Server.Services.Interfaces;
using Matlabs.OwlRacer.Common.Model;
using Matlabs.OwlRacer.Common.Options;
using Matlabs.OwlRacer.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Matlabs.OwlRacer.Server.Services
{
    public class GameService : IGameService
    {
        private const float TURN_RATE = 0.07f;
        private const int MAX_CHECKPOINTS = 20;

        private readonly object _raceCarLock = new();
        private readonly ILogger<GameService> _logger;
        private readonly IResourceService _resourceService;
        private readonly ISessionService _sessionService;
        private readonly GameOptions _gameOptions;

        private Boolean _uncrashed = true;

        public GameService(
            ILogger<GameService> logger,
            ISessionService sessionService,
            IResourceService resourceService,
            IOptions<GameOptions> gameOptions)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _resourceService = resourceService ?? throw new ArgumentException(nameof(resourceService));
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            _gameOptions = gameOptions.Value;
        }

        public void Reset(Guid raceCarId)
        {
            var car = _sessionService.GetRaceCarById(raceCarId);
            var session = _sessionService.GetSessionByCarId(raceCarId);

            car.Distance = new CarDistance();
            car.IsCrashed = false;
            car.UnCrashed = true;
            car.WrongDirection = false;
            car.Position = new Vector2(session.RaceTrack.StartPosition.X, session.RaceTrack.StartPosition.Y);
            car.Rotation = session.RaceTrack.StartRotation;
            car.ScoreStep = 0;
            car.ScoreOverall = 0;
            car.Ticks = 0;
            car.Velocity = 0.0f;
            car.Checkpoint = 0;
            car.PreviousCheckpoint = 0;
            car.TraveledDistance = 0;
            car.NumRounds = 0;
            car.NumCrashes = 0;

            Tick(raceCarId, 0.1f, StepData.Types.StepCommand.Idle);
        }

        public Task<RaceCar> StepAsync(Guid raceCarId, StepData.Types.StepCommand stepCommand)
        {
            var raceCar = _sessionService.GetRaceCarById(raceCarId);
            if (raceCar != null)
            {
                raceCar.LastStepCommand = (int)stepCommand;
            }

            return Task.FromResult(raceCar);
        }

        public RaceCar Tick(Guid raceCarId, float delta, StepData.Types.StepCommand stepCommand)
        {
            var deltaFactor = delta;
            var raceCar = _sessionService.GetRaceCarById(raceCarId);
            var session = _sessionService.GetSession(raceCar.SessionId);

            if (raceCar == null)
            {
                throw new InvalidOperationException($"The RaceCar with ID={raceCarId} does not exist.");
            }
            
            MoveCar(raceCar, session.GameTimeSetting * 10.0f * deltaFactor, stepCommand, session, deltaFactor);

            var direction = new Vector2(
                (float)Math.Cos(raceCar.Rotation),
                (float)Math.Sin(raceCar.Rotation)
            );

            DetectCheckpoint(raceCar, session);
            Distance(raceCar, session, direction);

            // Crash check
            raceCar.IsCrashed = DetectCollision(raceCar, session);

            if (raceCar.IsCrashed)
            {
                raceCar.Velocity = 0;

                if (raceCar.UnCrashed)
                {
                    raceCar.UnCrashed = !raceCar.UnCrashed;
                    raceCar.NumCrashes += 1;
                }
            }
            else
            {
                raceCar.UnCrashed = true;
            }

            ScoreCar(raceCar, session);

            raceCar.Ticks++;

            raceCar.LastAction = DateTimeOffset.UtcNow;

            return raceCar;
        }

        private static void ScoreCar(RaceCar raceCar, Session session)
        {
            if (!session.HasRaceStarted)
            {
                // If the race hasn't started, don't score the car.
                return;
            }

            /*Score calculation for the RL model
            /Score is based und Traveld distance with a substrackted time Part (faster and quicker is better)
            */

            // Initial thought
            //raceCar.Score = (int)(raceCar.Ticks) + 100 * (raceCar.Checkpoint);
            //raceCar.ScoreStep = (int)(raceCar.Velocity * 100 + raceCar.Ticks) + 100 * (raceCar.Checkpoint);

            //Substract last value to get the diverence beetween the updates
            raceCar.ScoreOverall -= raceCar.ScoreStep;

            raceCar.ScoreStep = (int) raceCar.TraveledDistance - (raceCar.Ticks / 6);

            if (raceCar.IsCrashed || raceCar.WrongDirection == true)
            {
                raceCar.ScoreOverall -= 1000;
            }

            raceCar.ScoreOverall += raceCar.ScoreStep;
            session.Scores[raceCar] = raceCar.ScoreOverall;
        }

        public void AccelerateCar(Guid raceCarId)
        {
            lock (_raceCarLock)
            {
                var raceCar = _sessionService.GetRaceCarById(raceCarId);

                if (raceCar == null)
                {
                    throw new InvalidOperationException($"The RaceCar with ID={raceCarId} does not exist.");
                }

                if (raceCar.Velocity <= raceCar.MaxVelocity)
                {
                    raceCar.Velocity += raceCar.Acceleration;
                }
                
                // TODO I kept this code since we probably want to change the center
                // of rotation sometime.
                //car.Position = Vector2.Transform(
                //    car.Position + new Vector2(amount, 0),
                //    Matrix3x2.CreateRotation(car.Rotation, car.Position)
                //);
            }
        }

        public void DecelerateCar(Guid raceCarId)
        {
            lock (_raceCarLock)
            {
                var raceCar = _sessionService.GetRaceCarById(raceCarId);

                if (raceCar == null)
                {
                    throw new InvalidOperationException($"The RaceCar with ID={raceCarId} does not exist.");
                }
                
                if (raceCar.Velocity > 0 && (raceCar.Velocity - raceCar.Acceleration) >= 0)
                {
                    raceCar.Velocity -= raceCar.Acceleration;
                } 
                else 
                {
                    raceCar.Velocity = 0;
                }
            }
        }

        public void MoveCar(RaceCar raceCar, float amount, StepData.Types.StepCommand stepCommand, Session session, float deltaFactor)
        {
            if (!session.HasRaceStarted)
            {
                // If the race hasn't started, don't move the car.
                return;
            }

            lock (_raceCarLock)
            {
                switch (stepCommand)
                {
                    case StepData.Types.StepCommand.TurnRight:
                    {
                        raceCar.Rotation += session.GameTimeSetting * TURN_RATE * deltaFactor;
                        break;
                    }

                    case StepData.Types.StepCommand.TurnLeft:
                    {
                        raceCar.Rotation -= session.GameTimeSetting * TURN_RATE * deltaFactor;
                        break;
                    }

                    case StepData.Types.StepCommand.AccelerateRight:
                    {
                        AccelerateCar(raceCar.Id);
                        raceCar.Rotation += session.GameTimeSetting * TURN_RATE * deltaFactor;
                        break;
                    }

                    case StepData.Types.StepCommand.AccelerateLeft:
                    {
                        AccelerateCar(raceCar.Id);
                        raceCar.Rotation -= session.GameTimeSetting * TURN_RATE * deltaFactor;
                        break;
                    }

                    case StepData.Types.StepCommand.Accelerate:
                    {
                        AccelerateCar(raceCar.Id);
                        break;
                    }

                    case StepData.Types.StepCommand.Decelerate:
                    {
                        DecelerateCar(raceCar.Id);
                        break;
                    }
                }

                var direction = new Vector2(
                    (float)Math.Cos(raceCar.Rotation),
                    (float)Math.Sin(raceCar.Rotation)
                );

                //Calculation of traveled Distance
                var posDelta = direction * (raceCar.Velocity * amount);
                var newPos = raceCar.Position + posDelta;
                var dX = newPos.X - raceCar.Position.X;
                var dY = newPos.Y - raceCar.Position.Y;
                var distance = Math.Sqrt(dX * dX + dY * dY);
                raceCar.TraveledDistance += distance;
                //write back new position
                raceCar.Position = newPos;
                // Slow deceleration over time
                if (raceCar.Velocity > 0 && (raceCar.Velocity - raceCar.Acceleration / 50 * amount) >= 0)
                {
                    raceCar.Velocity -= raceCar.Acceleration / 50 * amount;
                }

                // TODO I kept this code since we probably want to change the center
                // of rotation sometime.
                //car.Position = Vector2.Transform(
                //    car.Position + new Vector2(amount, 0),
                //    Matrix3x2.CreateRotation(car.Rotation, car.Position)
                //);
            }
        }

        public void Distance(RaceCar car, Session session, Vector2 direction)
        {
            var carImage = _resourceService.GetRaceCarImageData();
            var trackImage = _resourceService.GetRaceTrackImageData(session.RaceTrack.TrackNumber);

            int viewDistance = 1000;
            var frontOffset = carImage.Width / 2;
            var heightOffset = carImage.Height / 2;
            int beamLength = frontOffset + viewDistance;
            int beamLengthHeight = heightOffset + viewDistance;
            int sideOffset = (int)Math.Ceiling(Math.Sqrt(Math.Pow(frontOffset, 2) + Math.Pow(heightOffset, 2)));
            int beamLengthSide = sideOffset + viewDistance;

            var distance = new CarDistance
            {
                Front = beamLength,
                FrontLeft = beamLengthSide,
                FrontRight = beamLengthSide,
                Left = beamLengthHeight,
                Right = beamLengthHeight,
                MaxViewDistance = beamLengthSide
            };

            var carPosition = new Vector2
            {
                X = car.Position.X,
                Y = car.Position.Y
            };

            if (IsCarOutsideBoundaries(car))
            {
                Reset(car.Id);
            }

            // beam to the front
            for (int i = frontOffset; i < beamLength; i++)
            {
                var frontBeam = carPosition + direction * i;

                if(IsBeamOutsideBoundaries(car, frontBeam))
                {
                    distance.Front = i - sideOffset;
                    break;
                }

                if (trackImage.GetPixel((int)(frontBeam.X), (int)(frontBeam.Y)).A > MAX_CHECKPOINTS)
                {
                    distance.Front = i - frontOffset;
                    break;
                }
            }

            // frontRight beam
            for (int i = sideOffset; i < beamLengthSide; i++)
            {
                Vector2 directionRight = new Vector2(
                    (float)Math.Cos(car.Rotation + 2 * Math.PI - Math.Sin(Math.PI - frontOffset)),
                    (float)Math.Sin(car.Rotation + 2 * Math.PI - Math.Sin(Math.PI - frontOffset))
                );

                var newPositionRight = carPosition + directionRight * i;

                if (IsBeamOutsideBoundaries(car, newPositionRight))
                {
                    distance.FrontRight = i - sideOffset;
                    break;
                }

                if (trackImage.GetPixel((int)(newPositionRight.X), (int)(newPositionRight.Y)).A > MAX_CHECKPOINTS)
                {
                    distance.FrontRight = i - sideOffset;
                    break;
                }
            }

            // frontLeft beam
            for (int i = sideOffset; i < beamLengthSide; i++)
            {
                Vector2 directionLeft = new Vector2(
                    (float)Math.Cos(car.Rotation + (Math.Sin(Math.PI - frontOffset))),
                    (float)Math.Sin(car.Rotation + Math.Sin(Math.PI - frontOffset))
                );

                var newPositionLeft = carPosition + directionLeft * i;

                if(IsBeamOutsideBoundaries(car, newPositionLeft))
                {
                    distance.FrontLeft = i - sideOffset;
                    break;
                }

                if (trackImage.GetPixel((int)(newPositionLeft.X), (int)(newPositionLeft.Y)).A > MAX_CHECKPOINTS)
                {
                    distance.FrontLeft = i - sideOffset;
                    break;
                }
            }

            // right beam
            for (int i = heightOffset; i < beamLengthHeight; i++)
            {
                Vector2 directionRight = new Vector2(
                    (float)Math.Cos(car.Rotation + Math.PI / 2),
                    (float)Math.Sin(car.Rotation + Math.PI / 2)
                );

                var newPositionRight = carPosition + directionRight * i;

                if (IsBeamOutsideBoundaries(car, newPositionRight))
                {
                    distance.Right = i - sideOffset;
                    break;
                }

                if (trackImage.GetPixel((int)(newPositionRight.X), (int)(newPositionRight.Y)).A > MAX_CHECKPOINTS)
                {
                    distance.Right = i - heightOffset;
                    break;
                }
            }

            // left beam
            for (int i = heightOffset; i < beamLengthHeight; i++)
            {
                Vector2 directionLeft = new Vector2(
                    (float)Math.Cos(car.Rotation + 3 * Math.PI / 2),
                    (float)Math.Sin(car.Rotation + 3 * Math.PI / 2)
                );

                var newPositionLeft = carPosition + directionLeft * i;

                if (IsBeamOutsideBoundaries(car, newPositionLeft))
                {
                    distance.Left = i - sideOffset;
                    break;
                }

                if (trackImage.GetPixel((int)(newPositionLeft.X), (int)(newPositionLeft.Y)).A > MAX_CHECKPOINTS)
                {
                    distance.Left = i - heightOffset;
                    break;
                }
            }

            car.Distance = distance;
        }

        public bool DetectCollision(RaceCar car, Session session)
        {
            var carPosition = new VectorData2D
            {
                X = car.Position.X,
                Y = car.Position.Y
            };

            var angle = CalcAngle(car);

            var carImage = _resourceService.GetRaceCarImageData();
            var trackImage = _resourceService.GetRaceTrackImageData(session.RaceTrack.TrackNumber);

            // front left and right corner from car
            float posX = carImage.Width / 4f;
            float posLY = -(carImage.Height / 4f);
            float posRY = carImage.Height / 4f;

            // calculate new coordinates from angle
            var frontLeftX = ((posX * Math.Cos(angle)) - (posLY * Math.Sin(angle))) + carPosition.X;
            var frontLeftY = ((posLY * Math.Cos(angle)) + (posX * Math.Sin(angle))) + carPosition.Y;

            var frontRightX = ((posX * Math.Cos(angle)) - (posRY * Math.Sin(angle))) + carPosition.X;
            var frontRightY = ((posX * Math.Sin(angle)) + (posRY * Math.Cos(angle))) + carPosition.Y;

            // guard to prevent game breaking if car goes off the map
            frontLeftX = Math.Clamp(frontLeftX, 0, trackImage.Width - 1);
            frontLeftY = Math.Clamp(frontLeftY, 0, trackImage.Height - 1);
            frontRightX = Math.Clamp(frontRightX, 0, trackImage.Width - 1);
            frontRightY = Math.Clamp(frontRightY, 0, trackImage.Height - 1);

            // check front left anf right corner of the car
            var hasCrashedFrontRight = trackImage.GetPixel((int)frontLeftX, (int)frontLeftY).A > MAX_CHECKPOINTS;
            var hasCrashedFrontLeft = trackImage.GetPixel((int)frontRightX, (int)frontRightY).A > MAX_CHECKPOINTS;

            return hasCrashedFrontLeft || hasCrashedFrontRight;
        }

        public void DetectCheckpoint(RaceCar raceCar, Session session)
        {
            var checkPointImage = _resourceService.GetCheckpointImageData(session.RaceTrack.TrackNumber);
            
            var current = (int)checkPointImage.GetPixel((int)raceCar.Position.X, (int)raceCar.Position.Y).R;
            var previous = raceCar.PreviousCheckpoint;

            if( current == previous + 1){
                raceCar.Checkpoint += 1;
                raceCar.WrongDirection = false;
                _logger.LogInformation($"Race car {raceCar.Id} is entering {raceCar.Checkpoint}");
            }
            else if ( current == previous - 1){
                raceCar.Checkpoint -= 1;
                raceCar.WrongDirection = true;
                _logger.LogInformation($"Race car {raceCar.Id} returned to {raceCar.Checkpoint}");
            }
            else if (current == previous){
                raceCar.Checkpoint = raceCar.Checkpoint;
            }

            else if (previous == 0 && current-1 != 0)
            {
                raceCar.Checkpoint += -1;
                raceCar.WrongDirection = true;
                _logger.LogInformation($"!!!Race car {raceCar.Id} returned to {raceCar.Checkpoint}");   
            }
            else
            {
                if (raceCar.Checkpoint < 0)
                {
                    _logger.LogInformation($"Back to start position.");
                    
                }
                else
                {
                    _logger.LogInformation($"New round.");
                    raceCar.NumRounds += 1;
                }
                raceCar.Checkpoint = 0;
                raceCar.WrongDirection = false;
            }

            raceCar.PreviousCheckpoint = current;
        }

        private bool IsCarOutsideBoundaries(RaceCar car)
        {
            var session = _sessionService.GetSessionByCarId(car.Id);
            var raceTrackImage = _resourceService.GetRaceTrackImageData(session.RaceTrack.TrackNumber);
            var carImage = _resourceService.GetRaceCarImageData();
            var position = car.Position;

            return position.X <= 0 + (carImage.Width / 2)
                   || position.Y <= 0 + (carImage.Width / 2)
                   || position.X >= (raceTrackImage.Width - (carImage.Width / 2) - 1)
                   || position.Y >= (raceTrackImage.Height - (carImage.Width / 2) - 1);
        }

        private bool IsBeamOutsideBoundaries(RaceCar car, Vector2 position)
        {
            var session = _sessionService.GetSessionByCarId(car.Id);
            var raceTrackImage = _resourceService.GetRaceTrackImageData(session.RaceTrack.TrackNumber);

            return position.Y < 0
                   || position.X < 0
                   || position.X >= raceTrackImage.Width
                   || position.Y >= raceTrackImage.Height;
        }
            

        private float CalcAngle(RaceCar car)
        {
            var rotation = car.Rotation;

            var direction = new Vector2(
                (float) Math.Cos(rotation),
                (float) Math.Sin(rotation)
            );
            // angle in radians
            /** we have to invert the Y direction, because the y-axes goes down
             * if the direction is in the I or II quadrant we have to rotate by 90deg
             * if the direction is in the III or IV quadrant we have to add 180deg
             */
            float angle;
            _ = direction.Y >= 0
                ? angle = (float)(Math.Atan(direction.X / (direction.Y * -1)) + Math.PI / 2)
                : angle = (float)(Math.Atan(direction.X / (direction.Y * -1)) + Math.PI * 1.5);

            return angle;
        }

    }
}
