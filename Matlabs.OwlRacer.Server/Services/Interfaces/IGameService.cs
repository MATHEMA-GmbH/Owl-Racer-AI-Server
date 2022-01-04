using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Matlabs.OwlRacer.Common.Model;
using Matlabs.OwlRacer.Protobuf;

namespace Matlabs.OwlRacer.Server.Services.Interfaces
{
    public interface IGameService
    {
        void Reset(Guid raceCarId);
        Task<RaceCar> StepAsync(Guid raceCarId, StepData.Types.StepCommand stepCommand);
        RaceCar Tick(Guid raceCarId, float delta, StepData.Types.StepCommand stepCommand);
    }
}
