using System.Drawing;
using Matlabs.OwlRacer.Common.Model;

namespace Matlabs.OwlRacer.Server.Services.Interfaces
{
    public interface IResourceService
    {
        RaceTrack GetRaceTrack(int trackNumber);
        RaceTrack[] GetAllRaceTracks();
        byte[] GetRaceCarRawImageData();
        BitmapBuffer GetRaceCarImageData();
        byte[] GetStartLineRawImageData();
        BitmapBuffer GetStartLineImageData();
        byte[] GetRaceTrackRawImageData(int trackNumber);
        BitmapBuffer GetRaceTrackImageData(int trackNumber);
        byte[] GetCheckpointRawImageData(int trackNumber);
        BitmapBuffer GetCheckpointImageData(int trackNumber);

    }
}
