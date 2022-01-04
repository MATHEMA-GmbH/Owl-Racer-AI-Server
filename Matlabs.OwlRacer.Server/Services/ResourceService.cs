using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Matlabs.OwlRacer.Common.Model;
using Matlabs.OwlRacer.Common.Options;
using Matlabs.OwlRacer.Server.Services.Interfaces;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp.Formats.Bmp;

namespace Matlabs.OwlRacer.Server.Services
{
    public class ResourceService : IResourceService
    {
        private readonly Dictionary<int, RaceTrack> _trackCache = new();
        private readonly GameOptions _gameOptions;
        private readonly byte[] _raceCarRawImageCache = null;
        private readonly BitmapBuffer _raceCarImageCache = null;

        private readonly byte[] _startLineRawImageCache = null;
        private readonly BitmapBuffer _startLineImageCache = null;

        private readonly Dictionary<int, BitmapBuffer> _trackImagesCache = new();
        private readonly Dictionary<int, BitmapBuffer> _checkpointImagesCache = new();
        
        public ResourceService(IOptions<GameOptions> gameOptions)
        {
            _gameOptions = gameOptions.Value ?? throw new ArgumentNullException(nameof(gameOptions));
            
            for (var i = 0; i < gameOptions.Value.Tracks.Length; i++)
            {
                _trackCache[i] = CreateRaceTrack(i);
                _trackImagesCache[i] = CreateTrackImage(i);
                _checkpointImagesCache[i] = CreateCheckpointImage(i);
            }
            
            _raceCarRawImageCache = File.ReadAllBytes("Resources/racecar.png");
            _startLineRawImageCache = File.ReadAllBytes("Resources/startline.png");

            _raceCarImageCache = CreateCarImage();
            _startLineImageCache = CreateStartLineImage();
        }

        public RaceTrack GetRaceTrack(int trackNumber) => _trackCache.TryGetValue(trackNumber, out var track) ? track : null;
        public RaceTrack[] GetAllRaceTracks() => _trackCache.Values.ToArray();

        public byte[] GetRaceCarRawImageData() => _raceCarRawImageCache;
        public BitmapBuffer GetRaceCarImageData() => _raceCarImageCache;

        public byte[] GetStartLineRawImageData() => _startLineRawImageCache;
        public BitmapBuffer GetStartLineImageData() => _startLineImageCache;


        public byte[] GetRaceTrackRawImageData(int trackNumber) => _trackCache[trackNumber]?.ImageData;
        public BitmapBuffer GetRaceTrackImageData(int trackNumber) => _trackImagesCache[trackNumber];

        public byte[] GetCheckpointRawImageData(int trackNumber) => _trackCache[trackNumber]?.CheckpointImageData;
        public BitmapBuffer GetCheckpointImageData(int trackNumber) => _checkpointImagesCache[trackNumber];

        private RaceTrack CreateRaceTrack(int trackNumber) => new()
        {
            TrackNumber = trackNumber,
            ImageData = File.ReadAllBytes($"Resources/level{trackNumber}.png"),
            CheckpointImageData = File.ReadAllBytes($"Resources/level{trackNumber}_cpm.png"),
            StartPosition = _gameOptions.Tracks[trackNumber].StartPosition,
            StartRotation = _gameOptions.Tracks[trackNumber].StartRotation,
            StartLine = new
            (
                _gameOptions.Tracks[trackNumber].StartLinePosition.Start,
                _gameOptions.Tracks[trackNumber].StartLinePosition.End
            )
        };

        private BitmapBuffer CreateCarImage()
        {
            using var ms = new MemoryStream(GetRaceCarRawImageData());
            using var bmp = (Bitmap) Image.FromStream(ms);
            return new BitmapBuffer(bmp);
        }

        private BitmapBuffer CreateTrackImage(int trackNumber)
        {
            using var ms = new MemoryStream(GetRaceTrack(trackNumber).ImageData);
            using var bmp = (Bitmap)Image.FromStream(ms);
            return new BitmapBuffer(bmp);

        }

        private BitmapBuffer CreateCheckpointImage(int trackNumber)
        {
            using var ms = new MemoryStream(GetRaceTrack(trackNumber).CheckpointImageData);
            using var bmp = (Bitmap)Image.FromStream(ms);
            return new BitmapBuffer(bmp);

        }

        private BitmapBuffer CreateStartLineImage()
        {
            using var ms = new MemoryStream(GetStartLineRawImageData());
            using var bmp = (Bitmap)Image.FromStream(ms);
            return new BitmapBuffer(bmp);

        }
    }
}
