using System.Threading.Tasks;

namespace Matlabs.OwlRacer.Server.Services.Interfaces
{
    public interface IGrpcServerService
    {
        void StartServer();
        Task ShutdownServerAsync();
    }
}
