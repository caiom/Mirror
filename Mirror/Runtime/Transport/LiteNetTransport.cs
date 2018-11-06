// wraps UNET's LLAPI for use as HLAPI TransportLayer
using LiteNetLib;
using System;
using UnityEngine;
using System.Collections.Generic;

namespace Mirror
{
    public enum EType
    {
        Connect,
        Disconnect,
        Receive,
        ReceiveUnconnected,
        Error,
        ConnectionLatencyUpdated,
        DiscoveryRequest,
        DiscoveryResponse,
        ConnectionRequest,
        NoEvent
    }

    public class LiteNetTransport : TransportLayer
    {
        EventBasedNetListener _listener = new EventBasedNetListener();
        NetManager _netManager;

        EType _lastEType;
        byte[] _receiveData;
        int _lastEventPeerId;

        // For the client
        bool _isConnected = false;

        // For the server
        bool _isActive = false;
        int _maxConnections;

        public void ClientConnect(string address, int port)
        {
            if(_netManager != null)
            {
                Debug.LogError("Called ClientConnect while there is still a valid manager.");
            }

            Debug.LogError("Client Connect called...");

            _listener = new EventBasedNetListener();
            _netManager = new NetManager(_listener);
            

            _netManager.Start();

            _netManager.Connect(address, port, "ck");

            _listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod) =>
            {
                Debug.LogError("Receveid from " + fromPeer.Id + " bytes " + dataReader.AvailableBytes + " devivery " + deliveryMethod.ToString());

                _lastEType = EType.Receive;
                _lastEventPeerId = fromPeer.Id;

                _receiveData = dataReader.GetRemainingBytes();
                dataReader.Recycle();
            };

            _listener.PeerConnectedEvent += (peer) =>
            {
                _lastEType = EType.Connect;
                _lastEventPeerId = peer.Id;
                _isConnected = true;
                Debug.LogError("Client " + peer.Id + " Connected!");
            };

            _listener.PeerDisconnectedEvent += (peer, disconnectInfo) =>
            {
                _lastEType = EType.Disconnect;
                _lastEventPeerId = peer.Id;
                _isConnected = false;

                Debug.LogError("Peer id: " + peer.Id + " disconnect info " + disconnectInfo.Reason.ToString());
            };

            _listener.NetworkErrorEvent += (endPoint, socketError) =>
            {
                _lastEType = EType.Error;
                _isConnected = false;
                Debug.LogError("Network error event: " + endPoint.Address.ToString() + " reason: " + socketError.ToString());
            };

        }


        public bool ClientConnected()
        {
            return _isConnected;
        }

        public void ClientDisconnect()
        {
            _netManager.Stop();
            _netManager = null;
            _listener = null;
            _isConnected = false;
        }

        public bool ClientGetNextMessage(out TransportEvent transportEvent, out byte[] data)
        {
            data = null;
            transportEvent = TransportEvent.Connected;

            _lastEType = EType.NoEvent;

            bool eventAvailable = _netManager.PollOneEvent();

            while(eventAvailable && _lastEType != EType.Connect && _lastEType != EType.Disconnect && _lastEType != EType.Receive)
            {
                eventAvailable = _netManager.PollOneEvent();
            }

            if (!eventAvailable)
                return false;

            switch(_lastEType)
            {
                case EType.Receive:
                    data = _receiveData;
                    transportEvent = TransportEvent.Data;
                    break;
                case EType.Connect:
                    transportEvent = TransportEvent.Connected;
                    break;
                case EType.Disconnect:
                    transportEvent = TransportEvent.Disconnected;
                    break;
            }

            return true;

        }

        public bool ClientSend(int channelId, byte[] data)
        {
            NetPeer peer = _netManager.FirstPeer;

            if (peer == null)
            {
                return false;
            }

            switch (channelId)
            {
                case 0:
                    peer.Send(data, DeliveryMethod.ReliableOrdered);
                    peer.Flush();
                    break;
                case 1:
                    peer.Send(data, DeliveryMethod.ReliableUnordered);
                    peer.Flush();
                    break;
                case 2:
                    peer.Send(data, DeliveryMethod.Sequenced);
                    peer.Flush();
                    break;
                case 3:
                    peer.Send(data, DeliveryMethod.Unreliable);
                    peer.Flush();
                    break;
            }

            return true;
        }

        public bool GetConnectionInfo(int connectionId, out string address)
        {
            List<NetPeer> peers = new List<NetPeer>();
            _netManager.GetPeersNonAlloc(peers, ConnectionState.Any);

            foreach(NetPeer peer in peers)
            {
                if(peer.Id == connectionId)
                {
                    address = peer.EndPoint.ToString();
                    return true;
                }
            }

            address = null;
            return false;
        }

        public bool ServerActive()
        {
            return _isActive;
        }

        public bool ServerDisconnect(int connectionId)
        {
            List<NetPeer> peers = new List<NetPeer>();
            _netManager.GetPeersNonAlloc(peers, ConnectionState.Any);

            foreach (NetPeer peer in peers)
            {
                if (peer.Id == connectionId)
                {
                    _netManager.DisconnectPeer(peer);
                    return true;
                }
            }

            return false;
        }

        public bool ServerGetNextMessage(out int connectionId, out TransportEvent transportEvent, out byte[] data)
        {
            data = null;
            transportEvent = TransportEvent.Connected;
            connectionId = -1;

            if (!_isActive)
            {
                return false;
            }

            _lastEType = EType.NoEvent;

            bool eventAvailable = _netManager.PollOneEvent();

            while (eventAvailable && _lastEType != EType.Connect && _lastEType != EType.Disconnect && _lastEType != EType.Receive)
            {
                eventAvailable = _netManager.PollOneEvent();
            }

            if (!eventAvailable)
                return false;

            switch (_lastEType)
            {
                case EType.Receive:
                    data = _receiveData;
                    transportEvent = TransportEvent.Data;
                    connectionId = _lastEventPeerId;
                    break;
                case EType.Connect:
                    transportEvent = TransportEvent.Connected;
                    connectionId = _lastEventPeerId;
                    break;
                case EType.Disconnect:
                    transportEvent = TransportEvent.Disconnected;
                    connectionId = _lastEventPeerId;
                    break;
            }

            return true;
        }

        public bool ServerSend(int connectionId, int channelId, byte[] data)
        {
            List<NetPeer> peers = new List<NetPeer>();
            _netManager.GetPeersNonAlloc(peers, ConnectionState.Any);
            NetPeer targetPeer = null;

            foreach (NetPeer peer in peers)
            {
                if (peer.Id == connectionId)
                {
                    targetPeer = peer;
                    break;
                }
            }

            if (targetPeer == null)
                return false;

            switch (channelId)
            {
                case 0:
                    targetPeer.Send(data, DeliveryMethod.ReliableOrdered);
                    targetPeer.Flush();
                    break;
                case 1:
                    targetPeer.Send(data, DeliveryMethod.ReliableUnordered);
                    targetPeer.Flush();
                    break;
                case 2:
                    targetPeer.Send(data, DeliveryMethod.Sequenced);
                    targetPeer.Flush();
                    break;
                case 3:
                    targetPeer.Send(data, DeliveryMethod.Unreliable);
                    targetPeer.Flush();
                    break;
            }

            return true;
        }

        public void ServerStart(string address, int port, int maxConnections)
        {
            if (_netManager != null)
            {
                Debug.LogError("Called ServerStart while there is still a valid manager.");
            }

            _maxConnections = maxConnections;
            _listener = new EventBasedNetListener();
            _netManager = new NetManager(_listener);

            if(_netManager.Start(port))
            {
                _isActive = true;
            }

            _listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod) =>
            {
                Debug.LogError("Receveid from " + fromPeer.Id + " bytes " + dataReader.AvailableBytes + " devivery " + deliveryMethod.ToString());
                _lastEType = EType.Receive;
                _lastEventPeerId = fromPeer.Id;

                _receiveData = dataReader.GetRemainingBytes();
                dataReader.Recycle();
            };

            _listener.PeerConnectedEvent += (peer) =>
            {
                _lastEType = EType.Connect;
                _lastEventPeerId = peer.Id;
                Debug.LogError("Client Connected!");
            };

            _listener.PeerDisconnectedEvent += (peer, disconnectInfo) =>
            {
                _lastEType = EType.Disconnect;
                _lastEventPeerId = peer.Id;

                Debug.LogError("Peer id: " + peer.Id + " disconnect info " + disconnectInfo.ToString());
            };

            _listener.NetworkErrorEvent += (endPoint, socketError) =>
            {
                _lastEType = EType.Error;
                Debug.LogError("Network error event: " + endPoint.Address.ToString() + " reason: " + socketError.ToString());
            };

            _listener.ConnectionRequestEvent += (request) =>
            {
                _lastEType = EType.ConnectionRequest;
                Debug.LogError("Connection request received");

                if (_netManager.GetPeersCount(ConnectionState.Any) < _maxConnections)
                {
                    request.Accept();
                    Debug.LogError("Accepting connection.");
                }
                else
                {
                    request.Reject();
                }
            };
        }

        public void ServerStartWebsockets(string address, int port, int maxConnections)
        {
            throw new NotImplementedException();
        }

        public void ServerStop()
        {
            _netManager.Stop();
            _netManager = null;
            _listener = null;
            _isActive = false;
        }

        public void Shutdown()
        {
            if (_netManager != null)
                _netManager.Stop();
            _netManager = null;
            _listener = null;
            _isActive = false;
        }
    }
}