using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkManager : MonoBehaviour
{
    public EventBasedNetListener listener;
    public NetPacketProcessor processor;

    public static NetworkManager Instance;
    
    public List<NetworkedEntity> trackedEntities = new List<NetworkedEntity>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(Instance);
        }
    }

    void Update()
    {
#if UNITY_SERVER
        if (isServerReady)
        {
            server.PollEvents();
        }
#else
        if (isClientReady)
        {
            client.PollEvents();
        }
#endif
    }
    
    #region Client
    #if !UNITY_SERVER

    public NetManager client;
    public bool isClientReady;

    public string displayName;
    public int clientId;

    public string playerPrefabPath;
    
    public void InitializeAsClient(string ip, int port)
    {
        listener = new EventBasedNetListener();
        client = new NetManager(listener);
        processor = new NetPacketProcessor();
        
        processor.RegisterNestedType(Serializers.SerializeV3, Serializers.DeserializeV3);
        processor.RegisterNestedType(Serializers.SerializeQ, Serializers.DeserializeQ);

        listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
        {
            processor.ReadAllPackets(reader, peer);
            reader.Recycle();
        };
        
        processor.SubscribeReusable<InitialWorldStatePayload, NetPeer>((payload, server) =>
        {
            Debug.Log("Received initial server state.");
            Debug.Log($"Server has assigned us ClientID {payload.clientId}");
            Debug.Log($"Loading Scene: {payload.currentScenePath}");

            clientId = payload.clientId;
            var loadTask = SceneManager.LoadSceneAsync(payload.currentScenePath);

            loadTask.completed += operation =>
            {
                ClientInfoPayload pl = new ClientInfoPayload()
                {
                    clientName = displayName
                };
                processor.Send(server, pl, DeliveryMethod.ReliableSequenced);

                CreateEntityRequest spawnPlayerEvent = new CreateEntityRequest()
                {
                    ownerId = clientId,
                    spawnablePath = playerPrefabPath,
                    entityPosition = Vector3.zero
                };
                processor.Send(server, spawnPlayerEvent, DeliveryMethod.ReliableSequenced);
                
                for(int i = 0; i < payload.trackedEntityPrefabs.Length; i++)
                {
                    var entity = ((GameObject)Instantiate(Resources.Load(payload.trackedEntityPrefabs[i]))).GetComponent<NetworkedEntity>();
                    entity.ownerId = payload.trackedOwnerIds[i];
                    entity.entityId = payload.trackedEntityIds[i];
                    entity.transform.position = payload.trackedEntityPositions[i];
        
                    trackedEntities.Add(entity);
                }
            };
        });
        
        processor.SubscribeReusable<EntityCreatedEvent>(EntityCreated);
        processor.SubscribeReusable<PlayerMovedEvent>(PlayerMoved);
        processor.SubscribeReusable<ClientDisconnectedEvent>(ClientDisconnected);

        client.Start();
        isClientReady = true;
        client.Connect(ip, port, "usndbx");
    }

    void EntityCreated(EntityCreatedEvent entityCreatedEvent)
    {
        var entity = ((GameObject)Instantiate(Resources.Load(entityCreatedEvent.spawnablePath))).GetComponent<NetworkedEntity>();
        entity.ownerId = entityCreatedEvent.ownerId;
        entity.entityId = entityCreatedEvent.entityId;
        entity.transform.position = entityCreatedEvent.entityPositon;
        entity.transform.rotation = entityCreatedEvent.entityRotation;
        
        trackedEntities.Add(entity);
    }

    void ClientDisconnected(ClientDisconnectedEvent disconnectEvent)
    {
        foreach (NetworkedEntity entity in trackedEntities.FindAll(x => x.ownerId == disconnectEvent.clientId))
        {
            trackedEntities.Remove(entity);
            Destroy(entity);
        }
    }
    
    void PlayerMoved(PlayerMovedEvent movedEvent)
    {
        var entity = trackedEntities.Find(x => x.entityId == movedEvent.entityId);
        entity.GetComponent<CharacterController>().Move(movedEvent.movementVector);
        if (trackedEntities.Find(x => x.entityId == movedEvent.entityId).ownerId != clientId)
        {
            entity.transform.eulerAngles = new Vector3(0, movedEvent.rotation, 0);
        }

        entity.transform.localScale = movedEvent.isCrouching ? new Vector3(1, 0.5f, 1) : Vector3.one;
    }

#endif
#endregion
    
    #region Server
    #if UNITY_SERVER || DEBUG

    public NetManager server;
    public bool isServerReady;

    public string initialScenePath;

    public NetworkedClientList clients = new NetworkedClientList();
    
    public void InitializeAsHost()
    { 
        listener = new EventBasedNetListener(); 
        server = new NetManager(listener);
        processor = new NetPacketProcessor();
        
        processor.RegisterNestedType(Serializers.SerializeV3, Serializers.DeserializeV3);
        processor.RegisterNestedType(Serializers.SerializeQ, Serializers.DeserializeQ);
        
        SceneManager.LoadScene(initialScenePath);
        
        listener.ConnectionRequestEvent += request =>
        {
            if (server.ConnectedPeersCount < 10)
            {
                request.AcceptIfKey("usndbx");
            }
            else
            {
                request.Reject();
            }
        };
        
        listener.PeerConnectedEvent += peer =>
        {
            Debug.Log($"{peer.EndPoint} has connected.");
            
            clients.Add(new NetworkedClient()
            {
                clientId = new System.Random().Next(10000000, 99999999),
                associatedIpEndpoint = peer.EndPoint.ToString()
            });

            Debug.Log($"Associated ClientID {clients.GetIdFromIp(peer.EndPoint)} with {peer.EndPoint}");

            int[] entityIds = new int[trackedEntities.Count];
            int[] ownerIds = new int[trackedEntities.Count];
            string[] entityPrefabs = new string[trackedEntities.Count];
            Vector3[] entityPositions = new Vector3[trackedEntities.Count];
            
            for (int i = 0; i < trackedEntities.Count; i++)
            {
                entityIds[i] = trackedEntities[i].entityId;
                ownerIds[i] = trackedEntities[i].ownerId;
                entityPrefabs[i] = trackedEntities[i].prefabPath;
                entityPositions[i] = trackedEntities[i].transform.position;
            }
            
            InitialWorldStatePayload pl = new InitialWorldStatePayload()
            {
                currentScenePath = SceneManager.GetActiveScene().path,
                clientId = clients.GetIdFromIp(peer.EndPoint),
                trackedEntityPrefabs = entityPrefabs,
                trackedEntityIds = entityIds,
                trackedOwnerIds = ownerIds,
                trackedEntityPositions = entityPositions
            };
            processor.Send(peer, pl, DeliveryMethod.ReliableOrdered);
        };
        
        listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
        {
            processor.ReadAllPackets(reader, peer);
            reader.Recycle();
        };

        listener.PeerDisconnectedEvent += (peer, info) =>
        {
            Debug.Log($"Client {clients.GetIdFromIp(peer.EndPoint)} has disconnected (Reason: {info.Reason})");
            foreach (NetworkedEntity entity in trackedEntities.FindAll(x => x.ownerId == clients.GetIdFromIp(peer.EndPoint)))
            {
                trackedEntities.Remove(entity);
                Destroy(entity.gameObject);
            }
            
            foreach (NetPeer client in server.ConnectedPeerList)
            {
                processor.Send(client, new ClientDisconnectedEvent()
                {
                    clientId = clients.GetIdFromIp(peer.EndPoint)
                }, DeliveryMethod.ReliableSequenced);
            }
            
            clients.Remove(clients.Find(x => x.clientId == clients.GetIdFromIp(peer.EndPoint)));
        };
        
        processor.SubscribeReusable<ClientInfoPayload, NetPeer>((payload, client) =>
        {
            Debug.Log($"Client {clients.GetIdFromIp(client.EndPoint)} has identified as: {payload.clientName}");
            clients.SetNameFromIp(client.EndPoint, payload.clientName);
        });

        processor.SubscribeReusable<CreateEntityRequest, NetPeer>(ProcessCreateRequest);
        processor.SubscribeReusable<PlayerMoveRequest, NetPeer>(ProcessMoveEvent);

        server.Start(4040);
        isServerReady = true;
    }

    void ProcessCreateRequest(CreateEntityRequest createRequest, NetPeer sender)
    {
        GameObject entity = (GameObject)Instantiate(Resources.Load(createRequest.spawnablePath));
        if (entity.GetComponent<NetworkedEntity>() != null && clients.GetIdFromIp(sender.EndPoint) == createRequest.ownerId)
        {
            var entityCreatedEvent = new EntityCreatedEvent()
            {
                spawnablePath = createRequest.spawnablePath,
                ownerId = createRequest.ownerId,
                entityId = new System.Random().Next(10000000, 99999999),
                entityPositon = createRequest.entityPosition,
                entityRotation = createRequest.entityRotation
            };

            var currentEntity = entity.GetComponent<NetworkedEntity>();
            currentEntity.entityId = entityCreatedEvent.entityId;
            currentEntity.ownerId = entityCreatedEvent.ownerId;
            currentEntity.prefabPath = createRequest.spawnablePath;
            trackedEntities.Add(currentEntity);
            
            foreach (NetPeer peer in server.ConnectedPeerList)
            {
                processor.Send(peer, entityCreatedEvent, DeliveryMethod.ReliableSequenced);
            }
        }
        else
        {
            Destroy(entity);
        }
    }

    void ProcessMoveEvent(PlayerMoveRequest moveRequest, NetPeer sender)
    {
        if (trackedEntities.Find(x => x.entityId == moveRequest.entityId) != null)
        {
            var player = trackedEntities.Find(x => x.entityId == moveRequest.entityId);

            if (clients.GetIdFromIp(sender.EndPoint) == player.ownerId)
            {
                var playerMovedEvent = new PlayerMovedEvent()
                {
                    entityId = moveRequest.entityId,
                    movementVector = moveRequest.movementVector,
                    rotation = moveRequest.rotation,
                    isCrouching = moveRequest.isCrouching
                };
                
                player.GetComponent<CharacterController>().Move(moveRequest.movementVector);
                player.transform.eulerAngles = new Vector3(0, moveRequest.rotation, 0);
                player.transform.localScale = moveRequest.isCrouching ? new Vector3(1, 0.5f, 1) : Vector3.one;
                
                foreach (NetPeer peer in server.ConnectedPeerList)
                {
                    processor.Send(peer, playerMovedEvent, DeliveryMethod.ReliableSequenced);
                }
            }
        }
    }

#endif
#endregion
}

#region Editor Shit
#if UNITY_EDITOR
[CustomEditor(typeof(NetworkManager))]
public class NetworkManagerEditor : Editor
{
    private bool showServerFoldout = false;
    private bool showClientFoldout = false;
    
    public override void OnInspectorGUI()
    {
        #if UNITY_SERVER || DEBUG
        showServerFoldout = EditorGUILayout.Foldout(showServerFoldout, "Server");
        if (showServerFoldout)
        {
            // Scene Picker
            var oldScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(((NetworkManager)target).initialScenePath);

            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            var newScene = EditorGUILayout.ObjectField("Initial Scene", oldScene, typeof(SceneAsset), false) as SceneAsset;

            if (EditorGUI.EndChangeCheck())
            {
                var newPath = AssetDatabase.GetAssetPath(newScene);
                serializedObject.FindProperty("initialScenePath").stringValue = newPath;
            }
            serializedObject.ApplyModifiedProperties();
            
            
        }
        #endif
        #if !UNITY_SERVER || DEBUG
        showClientFoldout = EditorGUILayout.Foldout(showClientFoldout, "Client");
        if (showClientFoldout)
        {
            serializedObject.Update();
            EditorGUILayout.IntField("Client ID", serializedObject.FindProperty("clientId").intValue);
            serializedObject.ApplyModifiedProperties();
        }
        #endif
    }
}
#endif
#endregion