using System;
using System.Net;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace AzureDpsFramework.Transport
{
    /// <summary>
    /// Base transport handler aligned with Microsoft.Azure.Devices.Provisioning.Client.Transport.ProvisioningTransportHandler.
    /// </summary>
    public abstract class ProvisioningTransportHandler : IDisposable
    {
        private ProvisioningTransportHandler? _innerHandler;
        private int _port;

        public IWebProxy? Proxy { get; set; }
        public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; set; }

        public ProvisioningTransportHandler InnerHandler
        {
            get => _innerHandler ?? throw new InvalidOperationException("InnerHandler not set.");
            set => _innerHandler = value ?? throw new ArgumentNullException(nameof(value));
        }

        public int Port
        {
            get => _port;
            set
            {
                if (value < 1 || value > 65535)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _port = value;
            }
        }

        public virtual Task<DeviceRegistrationResult> RegisterAsync(
            ProvisioningTransportRegisterMessage message,
            CancellationToken cancellationToken)
        {
            return InnerHandler.RegisterAsync(message, cancellationToken);
        }

        public virtual Task<DeviceRegistrationResult> RegisterAsync(
            ProvisioningTransportRegisterMessage message,
            TimeSpan timeout)
        {
            return InnerHandler.RegisterAsync(message, timeout);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _innerHandler?.Dispose();
            }
        }
    }
}
