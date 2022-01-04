using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Matlabs.OwlRacer.Common.Model;
using Matlabs.OwlRacer.Protobuf;

namespace Matlabs.OwlRacer.Server.Services.Interfaces
{
    public interface ISessionService
    {
        Session GetSessionByCarId(Guid carId);
        RaceCar GetRaceCarById(Guid carId);
        Session CreateSession(float gameTimeSetting, int trackNumber, string name);
        Session GetSession(Guid guid);
        bool TryGetSession(Guid guid, out Session session);
        Task DestroySessionAsync(Guid guid);
        RaceCar CreateRaceCar(Guid sessionId, Vector2 startPosition, float startRotation, float maxVelocity, float acceleration, string name, string color);
        void DestroyRaceCar(Guid guid);

        public ICollection<Session> SessionValues { get; }
    }
}
