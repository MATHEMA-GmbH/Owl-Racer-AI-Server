using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Matlabs.OwlRacer.Server.Services.Interfaces;
using Matlabs.OwlRacer.Common.Model;
using Matlabs.OwlRacer.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using Google.Protobuf.WellKnownTypes;

namespace Matlabs.OwlRacer.Server.Services
{
    public class SessionService : ISessionService
    {
        private class SessionData : IAsyncDisposable
        {
            public Task TimeoutTask { get; init; }
            public Task TickTask { get; init; }
            public CancellationTokenSource CancellationSource { get; init; }

            public async ValueTask DisposeAsync()
            {
                CancellationSource?.Cancel();

                try
                {
                    await TimeoutTask;
                }
                catch
                {
                    // Logging
                }

                try
                {
                    await TickTask;
                }
                catch
                {
                    // Logging
                }
            }
        }

        private readonly ILogger<SessionService> _logger;
        private readonly IResourceService _resourceService;
        private readonly IServiceProvider _serviceProvider;

        private ConcurrentDictionary<Session, SessionData> Data { get; } = new();
        public ConcurrentDictionary<Guid, Session> Sessions { get; } = new();
        public ICollection<Session> SessionValues => Sessions.Values;

        public SessionService(
            ILogger<SessionService> logger,
            IResourceService resourceService,
            IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _resourceService = resourceService ?? throw new ArgumentNullException(nameof(resourceService));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public Session GetSessionByCarId(Guid carId) => SessionValues.FirstOrDefault(session => session.RaceCars.Any(car => car.Id.Equals(carId)));
        public RaceCar GetRaceCarById(Guid carId) => SessionValues.SelectMany(x => x.RaceCars).SingleOrDefault(x => x.Id == carId);

        public Session CreateSession(float gameTimeSetting, int trackNumber, string name)
        {
            var session = new Session
            {
                GameTimeSetting = gameTimeSetting,
                Name = name,
                RaceTrack = _resourceService.GetRaceTrack(trackNumber),
                GameTime = new TimeSpan() - TimeSpan.FromSeconds(5)
            };

            var cts = new CancellationTokenSource();

            var data = new SessionData
            {
                CancellationSource = cts,
                TimeoutTask = CreateTimeoutTask(session, cts.Token),
                TickTask = CreateTickTask(session, cts.Token)
            };
            Sessions[session.Id] = session;
            Data[session] = data;
            

            _logger.LogInformation($"Session with ID={session.Id} has registered");

            return session;
        }

        public Session GetSession(Guid guid)
        {
            if (!Sessions.TryGetValue(guid, out var session))
            {
                throw new KeyNotFoundException($"Session with id {guid} does not exist.");
            }

            return session;
        }

        public bool TryGetSession(Guid guid, out Session session)
        {
            try
            {
                session = GetSession(guid);
                return true;
            }
            catch (KeyNotFoundException)
            {
                session = null;
                return false;
            }
        }

        public async Task DestroySessionAsync(Guid guid)
        {
            _logger.LogInformation($"Destroying Session with ID={guid}");

            if (!Sessions.TryGetValue(guid, out var session))
            {
                throw new InvalidOperationException($"Session with ID={guid} does not exist.");
            }

            try
            {
                await Data[session].DisposeAsync();
            }
            catch (Exception e)
            {
                _logger.LogError($"Error while disposing session data with id {guid}! Error: {e}");
            }
            finally
            {
                Data.Remove(session, out var _);
                Sessions.Remove(guid, out var _);
            }
        }

        public void DestroyRaceCar(Guid guid)
        {
            _logger.LogInformation($"Destroying RaceCar with ID={guid}");

            var session = GetSessionByCarId(guid);
            if (session == null)
            {
                throw new InvalidOperationException($"Session for car with ID '{guid}' does not exist.");
            }

            lock (session.RaceCars)
            {
                var car = GetRaceCarById(guid);
                session.RaceCars.Remove(car);
            }
        }

        public RaceCar CreateRaceCar(Guid sessionId, Vector2 startPosition, float startRotation, float maxVelocity, float acceleration, string name, string color)
        {
            if (sessionId == Guid.Empty) { throw new ArgumentNullException(nameof(sessionId)); }

            var newCar = new RaceCar(sessionId, maxVelocity, acceleration, name, color)
            {
                Position = new Vector2(startPosition.X, startPosition.Y),
                Rotation = startRotation
            };

            _logger.LogInformation($"Creating new RaceCar with ID={newCar.Id} for Session with ID={sessionId} with name={newCar.Name} and color={newCar.Color}");

            var session = GetSession(sessionId);
            lock (session.RaceCars)
            {
                session.RaceCars.Add(newCar);
            }

            return newCar;
        }

        private Task CreateTimeoutTask(Session session, CancellationToken cancellationToken) => Task.Run(async () =>
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var diffTime = 30;
                    var timeNow = DateTimeOffset.UtcNow;

                    lock (session.RaceCars)
                    {
                        var timedOutCars =
                            session.RaceCars
                                .Where(x => (timeNow - x.LastAction).TotalSeconds > diffTime)
                                .ToList();

                        foreach (var car in timedOutCars)
                        {
                            DestroyRaceCar(car.Id);
                            _logger.LogInformation($"Car timeout, destroying car: {car.Id}");
                        }
                    }

                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation($"Timeout task for session {session.Id} was cancelled.");
            }
            catch (Exception e)
            {
                _logger.LogCritical($"Timeout task for session {session.Id} critically failed: {e}");
            }
        }, cancellationToken);

        private Task CreateTickTask(Session session, CancellationToken cancellationToken) => Task.Run(async () =>
        {
            try
            {
                var gameService = _serviceProvider.GetRequiredService<IGameService>();
                _logger.LogDebug(
                    $"Starting new ticker thread for session {session.Id}. High resolution clock: {Stopwatch.IsHighResolution}");
                var stopWatch = Stopwatch.StartNew();

                var ticksPerSeconds = Stopwatch.Frequency;
                var lastTicks = stopWatch.ElapsedTicks;

                while (!cancellationToken.IsCancellationRequested)
                {
                    var delta = (float) (stopWatch.ElapsedTicks - lastTicks) / ticksPerSeconds;

                    if (!Data.TryGetValue(session, out var data))
                    {
                        continue;
                    }

                    lock (session.RaceCars)
                    {
                        Parallel.ForEach(session.RaceCars, car =>
                        {
                            gameService.Tick(car.Id, delta, (StepData.Types.StepCommand)car.LastStepCommand);
                        });
                    }

                    session.GameTime += TimeSpan.FromSeconds(delta);
                    lastTicks = stopWatch.ElapsedTicks;
                    await Task.Delay(5, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation($"Tick task for session {session.Id} was cancelled.");
            }
            catch (Exception e)
            {
                _logger.LogCritical($"Tick task for session {session.Id} critically failed: {e}");
            }
        }, cancellationToken);

        public void FinishRace(GuidData session)
        {
            var sessionData = GetSession(new Guid(session.GuidString.ToString()));
            sessionData.IsFinished = true;
        }

        public bool RaceIsFinished(GuidData session)
        {
            var sessionData = GetSession(new Guid(session.GuidString.ToString()));
            return sessionData.IsFinished;
        }
    }
}
