using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class GlobalDirection
{
    private static Vector3 direction;

    public static Vector3 Direction
    {
        get { return direction; }
        set { direction = value; }
    }
}
