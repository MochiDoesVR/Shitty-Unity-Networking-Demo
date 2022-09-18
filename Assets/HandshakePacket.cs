using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using LiteNetLib.Utils;
using UnityEngine;

#region Handshake

public class InitialWorldStatePayload
{
    public int clientId { get; set; }
    public string currentScenePath { get; set; }

    public string[] trackedEntityPrefabs { get; set; }
    public int[] trackedEntityIds { get; set; }
    public int[] trackedOwnerIds { get; set; }
    public Vector3[] trackedEntityPositions { get; set; }
}

public class ClientInfoPayload
{
    public string clientName { get; set; }
}

#endregion

#region Event Packets

public class CreateEntityRequest
{
    public int ownerId { get; set; }
    public string spawnablePath { get; set; }
    public Vector3 entityPosition { get; set; }
    public Quaternion entityRotation { get; set; }
}

public class EntityCreatedEvent
{
    public string spawnablePath { get; set; }
    public int ownerId { get; set; }
    public int entityId { get; set; }
    public Vector3 entityPositon { get; set; }
    public Quaternion entityRotation { get; set; }
}

public class PlayerMoveRequest
{
    public int entityId { get; set; }
    public Vector3 movementVector { get; set; }
    public float rotation { get; set; }
    public bool isCrouching { get; set; }
}

public class PlayerMovedEvent
{
    public int entityId { get; set; }
    public Vector3 movementVector { get; set; }
    public float rotation { get; set; }
    public bool isCrouching { get; set; }
}

public class ClientDisconnectedEvent
{
    public int clientId;
}

#endregion