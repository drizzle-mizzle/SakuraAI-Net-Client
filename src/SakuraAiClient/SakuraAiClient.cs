using System.Diagnostics;
using RestSharp;

namespace SakuraAi
{
    /// <inheritdoc />
    public class SakuraAiClient : IDisposable
    {
        private const int RefreshTimeout = 60_000; // minute
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        protected internal RestClient ApiRestClient { get; set; } = null!;
        protected internal RestClient SakuraRestClient { get; set; } = null!;
        protected internal RestClient ClerkRestClient { get; set; } = null!;
        protected internal string Cookie { get; set; } = null!;
        protected internal bool Init { get; set; } = false;


        protected internal void Refresh()
        {
            if (_sw.ElapsedMilliseconds < RefreshTimeout)
            {
                return;
            }

            lock (ApiRestClient)
            {
                lock (SakuraRestClient)
                {
                    lock (ClerkRestClient)
                    {
                        this.InitializeAsync().Wait();
                    }
                }
            }

            _sw.Restart();
        }


        #region Dispose

        private bool _disposedValue = false;

        private void Dispose(bool disposing)
        {
            if (_disposedValue)
            {
                return;
            }

            ClerkRestClient.Dispose();
            SakuraRestClient.Dispose();
            ApiRestClient.Dispose();

            _disposedValue = true;
        }


        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion Dispose
    }
}
