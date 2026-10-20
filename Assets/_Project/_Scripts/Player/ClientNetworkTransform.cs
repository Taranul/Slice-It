using Unity.Netcode.Components;
using UnityEngine;

namespace SliceIt.Player
{
    /// <summary>
    /// Owner-authoritative NetworkTransform.
    /// Allows the local player (host or client) to drive their CharacterController
    /// and replicate their position and rotation to all peers.
    /// </summary>
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}
