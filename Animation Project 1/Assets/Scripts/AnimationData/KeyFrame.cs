﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class KeyFrame
{
    public Vector3 keyPosition;
    public Vector3 keyRotation;
	public int atFrame;

    public KeyFrame()
    {
        atFrame = 0;
        keyPosition = Vector3.zero;
    }

    public KeyFrame(Vector3 newPosition)
    {
        atFrame = 0;
        keyPosition = newPosition;
    }
}
