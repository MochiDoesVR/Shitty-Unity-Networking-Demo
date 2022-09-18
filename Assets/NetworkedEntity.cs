using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using System.Threading.Tasks;

public class NetworkedEntity : MonoBehaviour
{
    public int ownerId { get; set; }
    public int entityId { get; set; }

    #if UNITY_SERVER || DEBUG
    public string prefabPath { get; set; }
    #endif
}
