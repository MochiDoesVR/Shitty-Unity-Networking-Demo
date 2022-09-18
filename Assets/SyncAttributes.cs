using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[AttributeUsage(AttributeTargets.Field)]
public class SynchronizeFieldAttribute : Attribute
{
    
}

[AttributeUsage(AttributeTargets.Method)]
public class SynchronizedEventAttribute : Attribute
{
    
}