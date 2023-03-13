using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Matlabs.OwlRacer.Common;
using Matlabs.OwlRacer.Server.Services.Interfaces;
using Matlabs.OwlRacer.Common.Model;
using Matlabs.OwlRacer.Protobuf;
using Microsoft.Extensions.Logging;

namespace Matlabs.OwlRacer.Server.Services.Grpc
{
    public class GrpcCoreServiceImpl : GrpcCoreService.GrpcCoreServiceBase
    {
        private readonly ILogger<GrpcCoreServiceImpl> _logger;
        private readonly IResourceService _resourceService;
        private readonly ISessionService _sessionService;
        private readonly IGameService _gameService;

        public GrpcCoreServiceImpl(
            ILogger<GrpcCoreServiceImpl> logger,
            IResourceService resourceService,
            ISessionService sessionService,
            IGameService gameService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _resourceService = resourceService ?? throw new ArgumentNullException(nameof(resourceService));
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
        }

        public override async Task<SessionData> CreateSession(CreateSessionData request, ServerCallContext context)
        {
            if(request == null) { throw new ArgumentNullException(nameof(request)); }

            var raceTrack = _resourceService.GetRaceTrack(request.TrackNumber);
            if (raceTrack == null)
            {
                throw new InvalidOperationException($"The race track with number {request.TrackNumber} is invalid.");
            }

            var session = _sessionService.CreateSession(request.GameTimeSetting, request.TrackNumber, request.Name);

            return await Task.FromResult(new SessionData
            {
                Id = new GuidData
                {
                    GuidString = session.Id.ToString()
                },
                Name = session.Name,
                TrackNumber = session.RaceTrack.TrackNumber,
                GameTimeSetting = session.GameTimeSetting,
                GameTime = Timestamp.FromDateTimeOffset(new DateTimeOffset() + TimeSpan.FromTicks(Math.Abs(session.GameTime.Ticks))),
                IsGameTimeNegative = session.GameTime.Ticks < 0,
                Phase = session.IsPaused ? SessionData.Types.Phase.Pause : (session.GameTime.Ticks < 0 ? SessionData.Types.Phase.Prerace : SessionData.Types.Phase.Race)
            });
        }

        public override Task<RaceCarData> CreateCar(CreateCarData request, ServerCallContext context)
        {
            var sessionId = Guid.Parse(request.SessionId.GuidString);
            var session = _sessionService.GetSession(sessionId);

            var newCar = _sessionService.CreateRaceCar(
                sessionId,
                new Vector2(session.RaceTrack.StartPosition.X, session.RaceTrack.StartPosition.Y),
                session.RaceTrack.StartRotation,
                request.MaxVelocity,
                request.Acceleration,
                request.Name,
                request.Color);

            return Task.FromResult(newCar.ToGrpcData());
        }

        public override async Task<Empty> DestroyCar(GuidData request, ServerCallContext context)
        {
            _sessionService.DestroyRaceCar(Guid.Parse(request.GuidString));
            return await Task.FromResult(new Empty());
        }

        public override async Task<Empty> DestroySession(GuidData request, ServerCallContext context)
        {
            await _sessionService.DestroySessionAsync(Guid.Parse(request.GuidString));
            return await Task.FromResult(new Empty());
        }

        public override async Task<RaceCarData> GetCarData(GuidData request, ServerCallContext context)
        {
            var carData = _sessionService.GetRaceCarById(Guid.Parse(request.GuidString));
            return await Task.FromResult(carData?.ToGrpcData() ?? throw new InvalidOperationException("Unable to step, racecar not valid."));
        }

        public override async Task<RaceCarData> Step(StepData request, ServerCallContext context)
        {
            var result = await _gameService.StepAsync(Guid.Parse(request.CarId.GuidString), request.Command);
            return result?.ToGrpcData() ?? throw new InvalidOperationException("Unable to step, racecar not valid.");
        }

        public override async Task<Empty> Reset(GuidData request, ServerCallContext context)
        {
            _gameService.Reset(Guid.Parse(request.GuidString));
            return await Task.FromResult(new Empty());
        }

        public override Task<GuidListData> GetCarIds(GuidData request, ServerCallContext context)
        {
            var sessionsGuid = Guid.Parse(request.GuidString);

            return Task.FromResult(new GuidListData
            {
                Guids =
                {
                    _sessionService.GetSession(sessionsGuid).RaceCars.Select(x => new GuidData
                    {
                        GuidString = x.Id.ToString()
                    })
                }
            });
        }

        public override async Task<SessionData> GetSession(GuidData request, ServerCallContext context)
        {
            var sessionsGuid = Guid.Parse(request.GuidString);

            if (!_sessionService.TryGetSession(sessionsGuid, out var session))
            {
                return null;
            }
            
            return await Task.FromResult(new SessionData
            {
                Id = new GuidData
                {
                    GuidString = session.Id.ToString()
                },
                Name = session.Name,
                TrackNumber = session.RaceTrack.TrackNumber,
                GameTimeSetting = session.GameTimeSetting,
                Scores =
                {
                    session.Scores.Select(x => new ScoreData
                    {
                        CarId = new GuidData { GuidString = x.Key.Id.ToString() },
                        CarName = x.Key.Name,
                        NumCrashes = x.Key.NumCrashes,
                        NumRounds = x.Key.NumRounds,
                        Score = x.Value
                    })
                },
                GameTime = Timestamp.FromDateTimeOffset(new DateTimeOffset() + TimeSpan.FromTicks(Math.Abs(session.GameTime.Ticks))),
                IsGameTimeNegative = session.GameTime.Ticks < 0,
                Phase = session.IsPaused ? SessionData.Types.Phase.Pause : (session.GameTime.Ticks < 0 ? SessionData.Types.Phase.Prerace : SessionData.Types.Phase.Race)
            });
        }

        public override Task<GuidListData> GetSessionIds(Empty request, ServerCallContext context)
        {
            return Task.FromResult(new GuidListData
            {
                Guids =
                {
                    _sessionService.SessionValues.Select(x => new GuidData
                    {
                        GuidString = x.Id.ToString()
                    })
                }
            });
        }

        public override async Task<Empty> FinishRace(GuidData request, ServerCallContext context)
        {
            _sessionService.FinishRace(request);
            return await Task.FromResult(new Empty());
        }

        public override Task<FinishRaceData> RaceIsFinished(GuidData request, ServerCallContext context)
        {
            var response = new FinishRaceData { IsFinished = _sessionService.RaceIsFinished(request) };
            return Task.FromResult(response);
        }
    }
}
