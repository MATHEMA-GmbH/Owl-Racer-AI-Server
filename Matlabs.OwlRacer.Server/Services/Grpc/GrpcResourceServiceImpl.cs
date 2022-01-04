using System;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Matlabs.OwlRacer.Common.Options;
using Matlabs.OwlRacer.Protobuf;
using Matlabs.OwlRacer.Server.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Matlabs.OwlRacer.Server.Services.Grpc
{
    public class GrpcResourceServiceImpl : GrpcResourceService.GrpcResourceServiceBase
    {
        private readonly IResourceService _resourceService;

        public GrpcResourceServiceImpl(IResourceService resourceService)
        {
            _resourceService = resourceService;
        }

        public override async Task<ResourceImagesDataResponse> GetBaseImages(Empty request, ServerCallContext context)
        {
            return await Task.FromResult(new ResourceImagesDataResponse
            {
                Car = ByteString.CopyFrom(_resourceService.GetRaceCarRawImageData()),
                StartLine = ByteString.CopyFrom(_resourceService.GetStartLineRawImageData())
            });
        }

        public override async Task<TrackImageDataResponse> GetTrackImage(TrackIdData request, ServerCallContext context)
        {
            return await Task.FromResult(new TrackImageDataResponse
            {
                RaceTrack = ByteString.CopyFrom(_resourceService.GetRaceTrack(request.TrackNumber).ImageData),
            });
        }

        public override Task<TrackData> GetTrackData(TrackIdData request, ServerCallContext context)
        {
            var track = _resourceService.GetRaceTrack(request.TrackNumber);
            return Task.FromResult(new TrackData
            {
                StartPosition = new VectorData2D
                {
                    X = track.StartPosition.X,
                    Y = track.StartPosition.Y
                },
                StartRotation = track.StartRotation,
                TrackNumber = track.TrackNumber,
                LinePositionStart = new VectorData2D
                {
                    X = track.StartLine.Start.X,
                    Y = track.StartLine.Start.Y
                },
                LinePositionEnd = new VectorData2D
                {
                    X = track.StartLine.End.X,
                    Y = track.StartLine.End.Y
                }
            });
        }
    }
}
