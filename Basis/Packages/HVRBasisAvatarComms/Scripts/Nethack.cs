using System;

namespace HVR.Basis.Comms
{
    internal class Nethack
    {
        private readonly Action<bool> _onReadyBothAvatarAndNetwork;

        private bool _avatarReady;
        private bool _networkReady;
        private bool _isLocallyOwned;

        public Nethack(Action<bool> onReadyBothAvatarAndNetwork)
        {
            _onReadyBothAvatarAndNetwork = onReadyBothAvatarAndNetwork;
        }

        public void AfterAvatarReady()
        {
            if (_avatarReady) return;
            _avatarReady = true;
            if (_avatarReady && _networkReady) _onReadyBothAvatarAndNetwork(_isLocallyOwned);
        }

        public void AfterNetworkReady(bool isLocallyOwned)
        {
            if (_networkReady) return;
            _networkReady = true;
            _isLocallyOwned = isLocallyOwned;
            if (_avatarReady && _networkReady) _onReadyBothAvatarAndNetwork(_isLocallyOwned);
        }
    }
}
