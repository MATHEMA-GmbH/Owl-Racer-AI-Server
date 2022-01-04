using Matlabs.OwlRacer.Common.Model;
using Matlabs.OwlRacer.Protobuf;

namespace Matlabs.OwlRacer.Common
{
    public static class ExtensionMethods
    {
        public static RaceCarData ToGrpcData(this RaceCar @this) => new()
        {
            Id = new GuidData
            {
                GuidString = @this.Id.ToString()
            },
            Name = @this.Name,
            Color = @this.Color,
            Acceleration = @this.Acceleration,
            CheckPoint = @this.Checkpoint,
            NumRounds = @this.NumRounds,
            NumCrashes = @this.NumCrashes,
            Distance = new CarDistanceData
            {
                Front = @this.Distance.Front,
                FrontLeft = @this.Distance.FrontLeft,
                FrontRight = @this.Distance.FrontRight,
                Left = @this.Distance.Left,
                Right = @this.Distance.Right,
                MaxViewDistance = @this.Distance.MaxViewDistance
            },
            IsCrashed = @this.IsCrashed,
            UnCrashed = @this.UnCrashed,
            WrongDirection = @this.WrongDirection,
            MaxVelocity = @this.MaxVelocity,
            Position = new VectorData2D
            {
                X = @this.Position.X,
                Y = @this.Position.Y
            },
            Rotation = @this.Rotation,
            ScoreOverall = @this.ScoreOverall,
            ScoreStep = @this.ScoreStep,
            Ticks = @this.Ticks,
            Velocity = @this.Velocity,
            SessionId = new GuidData
            {
                GuidString = @this.SessionId.ToString()
            },
            LastStepCommand = @this.LastStepCommand
        };
    }
}
