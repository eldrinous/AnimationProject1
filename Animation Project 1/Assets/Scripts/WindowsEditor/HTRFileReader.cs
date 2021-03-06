﻿using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public enum currentHTRMode
{
    HEADER = 0,
    SEGMENTHIERARCHY,
    BASE_POSITION,
    FRAMING,
    END

}

public struct DataInput
{
    public string name;
    public Vector3 transform;
    public Vector3 rotation;
    public float boneLength;
    public float scaleFactor;
    public int frame;
}


public class HTRFileReader : EditorWindow
{
    AnimationDataHierarchal animData;
    string path = "Assets/dbuckstein monster/res/monster_anim.htr"; //default
    currentHTRMode curMode;
    GameObject jointObject;
    bool done = false;

    [MenuItem("Window/HTR Input")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow(typeof(HTRFileReader));
    }

    private void OnGUI()
    {
        curMode = currentHTRMode.HEADER;
        jointObject = EditorGUILayout.ObjectField("AnimationObject", jointObject, typeof(GameObject), true) as GameObject;
        animData = EditorGUILayout.ObjectField("AnimationData", animData, typeof(AnimationDataHierarchal), true) as AnimationDataHierarchal;
        path = EditorGUILayout.TextField("AssestPath", path);
        if(GUILayout.Button("Process"))
        {
            if(animData != null)
            {
                processFile();
            }
            else
            {
                Debug.Log("scriptable object missing!");
            }
        }
    }

    void processFile()
    {
        done = false;

        StreamReader reader = new StreamReader(path);
        //read header
        curMode = 0;
        Debug.Log(reader.ReadLine());
        Debug.Log(reader.ReadLine());
        Debug.Log(reader.ReadLine());
        Debug.Log(reader.ReadLine());
        Debug.Log(reader.ReadLine());
        Debug.Log(reader.ReadLine());
        animData.createBase(readToSpaceInt(reader.ReadLine())); // number of segments
        animData.generateFrames(readToSpaceInt(reader.ReadLine())); //number of frames
        animData.setFramePerSecond(readToSpaceInt(reader.ReadLine())); //frame rate
        Debug.Log(reader.ReadLine()); //rotation order
        animData.setCalibrationUnit(readToSpaceString(reader.ReadLine())); //calibration units
        Debug.Log(reader.ReadLine()); //rotation units
        Debug.Log(reader.ReadLine()); //globalaxisofGravity
        Debug.Log(reader.ReadLine()); //bone length axis
        animData.scaleFactor = readToSpaceFloat(reader.ReadLine()); //scale factor
        //end of header

        //for setting hierarchy
        int jointCount = 0; //keep track of the where the index it is putting in the basepose
        List<string> jointIndexList = new List<string>(); //keeps track of the string names for easier hierarchy building/checking
        string currentJoint = ""; //for keyframing
        gameObjectMain mainObjStructure = null;
        while (!done)
        {
            string textLine = "";
            if (readLine(ref textLine,ref reader, ref currentJoint))
            {
                if(curMode == currentHTRMode.SEGMENTHIERARCHY)
                {
                    string first = "", second = "";
                    parseTextToTwoBetweenTab(ref first, ref second, textLine); //find the two strings

                    //add and update joint
                    animData.poseBase[jointCount].name = first;
                    jointIndexList.Add(first);

                    //generate parent index using the jointIndexList
                    if(second != "GLOBAL")
                    {
                        animData.poseBase[jointCount].parentNodeIndex = jointIndexList.IndexOf(second);
                    }
                    else
                    {
                        animData.poseBase[jointCount].parentNodeIndex = -1;
                    }

                    jointCount++;
                }
                else if(curMode == currentHTRMode.BASE_POSITION)
                {
                    //create base pose
                    DataInput data = superParseDataIntoInputBase(textLine);
                    int index = jointIndexList.IndexOf(data.name);
                    animData.poseBase[index].boneLength = data.boneLength;

                    //generate local matrix using pared data
                    Matrix4x4 localMat = Matrix4x4.TRS(data.transform, Quaternion.Euler(data.rotation), new Vector4(1,1,1,1));
                    animData.poseBase[index].localBaseTransform = localMat;

                    //do forward kinematics
                    int parentIndex = animData.poseBase[index].parentNodeIndex;

                    GameObject parentObj = null;
                    if (parentIndex == -1)
                    {
                        //is root
                        animData.poseBase[index].globalBaseTransform = localMat;

                        //generate joint in scene (test) https://answers.unity.com/questions/402280/how-to-decompose-a-trs-matrix.html
                        GameObject newJoint = Instantiate(jointObject, animData.poseBase[index].globalBaseTransform.GetColumn(3), Quaternion.Euler((animData.poseBase[index].globalBaseTransform.GetRow(1))));
                        newJoint.name = data.name;
                  
                        newJoint.AddComponent<gameObjectMain>();
                        mainObjStructure = newJoint.GetComponent<gameObjectMain>();
                        mainObjStructure.newList();
                        mainObjStructure.addObject(newJoint);
                    }
                    else
                    {
                        //create global transform by taking the parent's transform and multiply with the local matrix
                        animData.poseBase[index].globalBaseTransform = (animData.poseBase[parentIndex].globalBaseTransform * localMat);
                        parentObj = mainObjStructure.getObject(parentIndex);

                        //generate joint in scene (test) https://answers.unity.com/questions/402280/how-to-decompose-a-trs-matrix.html
                        GameObject newJoint = Instantiate(jointObject, animData.poseBase[index].globalBaseTransform.GetColumn(3), Quaternion.Euler((animData.poseBase[index].globalBaseTransform.GetRow(1))), parentObj.transform);
                        newJoint.name = data.name;
                        mainObjStructure.addObject(newJoint);
                    }

                    animData.poseBase[index].currentTransform = animData.poseBase[index].globalBaseTransform;

                }
                else if(curMode == currentHTRMode.FRAMING)
                {
                    //get the keyframe and set it in
                    DataInput data = superParseDataIntoInputKeyFrame(textLine);
                    int index = jointIndexList.IndexOf(currentJoint);
                    animData.poseBase[index].keyFrames[data.frame].atFrame = data.frame;
                    animData.poseBase[index].keyFrames[data.frame].keyPosition = data.transform;
                    animData.poseBase[index].keyFrames[data.frame].keyRotation = data.rotation;
                    animData.poseBase[index].keyFrames[data.frame].scale = new Vector3(data.scaleFactor, data.scaleFactor, data.scaleFactor);
                }
            }
        }
        
    }

    DataInput superParseDataIntoInputKeyFrame(string textBlock)
    {
        DataInput newInput = new DataInput();
        int indexer = 0;
        //0 = frame
        // 1-3 = transform xyz
        // 4-7 = rotation xyz euler
        // 8 = scale
        int charCount = 0;
        string newText = "";
        while (charCount < textBlock.Length)
        {
            char dat = textBlock[charCount];
            if (dat == '\t')
            {
                //encountered tab, determine where the text it will go into based on index
                switch (indexer)
                {
                    case 0:
                        newInput.frame = int.Parse(newText);
                        break;
                    case 1:
                        newInput.transform += new Vector3(float.Parse(newText), 0, 0);
                        break;
                    case 2:
                        newInput.transform += new Vector3(0, float.Parse(newText), 0);
                        break;
                    case 3:
                        newInput.transform += new Vector3(0, 0, float.Parse(newText));
                        break;
                    case 4:
                        newInput.rotation += new Vector3(float.Parse(newText), 0, 0);
                        break;
                    case 5:
                        newInput.rotation += new Vector3(0, float.Parse(newText), 0);
                        break;
                    case 6:
                        newInput.rotation += new Vector3(0, 0, float.Parse(newText));
                        break;
                }
                //update to next index and reset text
                indexer++;
                newText = "";
            }
            else
            {
                newText += dat;
            }

            charCount++;
        }

        newInput.scaleFactor = float.Parse(newText); //add the last text
        return newInput;
    }

    DataInput superParseDataIntoInputBase(string textBlock)
    {
        DataInput newInput = new DataInput();
        int indexer = 0; //keeps track of which block to put it in
        // 0 = name
        // 1-3 = transform xyz
        // 4-7 = rotation xyz euler
        // 8 = bone length

        int charCount = 0;
        string newText = "";
        while(charCount < textBlock.Length)
        {
            char dat = textBlock[charCount];
            if(dat == '\t')
            {
                //encountered tab, determine where the text it will go into based on index
                switch(indexer)
                {
                    case 0:
                        newInput.name = newText;
                        break;
                    case 1:
                        newInput.transform += new Vector3(float.Parse(newText), 0, 0);
                        break;
                    case 2:
                        newInput.transform += new Vector3(0, float.Parse(newText), 0);
                        break;
                    case 3:
                        newInput.transform += new Vector3(0, 0, float.Parse(newText));
                        break;
                    case 4:
                        newInput.rotation += new Vector3(float.Parse(newText), 0, 0);
                        break;
                    case 5:
                        newInput.rotation += new Vector3(0, float.Parse(newText), 0);
                        break;
                    case 6:
                        newInput.rotation += new Vector3(0, 0, float.Parse(newText));
                        break;
                }
                //update to next index and reset text
                indexer++;
                newText = "";
            }
            else
            {
                newText += dat;
            }

            charCount++;
        }

        newInput.boneLength = float.Parse(newText); //add the last text
        return newInput;
    }

    void parseTextToTwoBetweenTab(ref string first, ref string second, string main)
    {
        bool spaceEncountered = false;
        for(int i = 0; i<main.Length; i++)
        {
            char dat = main[i];
            if(dat == '\t')
            {
                spaceEncountered = true;
            }
            else if(spaceEncountered)
            {
                second += dat;
            }
            else
            {
                first += dat;
            }
        }
    }

    //reads in an int, ignores the first text "name: " then gets the int after.
    int readToSpaceInt(string text)
    {
        string newText = "";
        bool spaceEncountered = false;
        for(int i = 0; i < text.Length; i++)
        {
            char dat = text[i];

            if(spaceEncountered)
            {
                newText += dat;
            }
            if(dat == ' ')
            {
                spaceEncountered = true;
            }
        }
        return int.Parse(newText);
    }

    float readToSpaceFloat(string text)
    {
        string newText = "";
        bool spaceEncountered = false;
        for (int i = 0; i < text.Length; i++)
        {
            char dat = text[i];

            if (spaceEncountered)
            {
                newText += dat;
            }
            if (dat == ' ')
            {
                spaceEncountered = true;
            }
        }
        return float.Parse(newText);
    }

    string readToSpaceString(string text)
    {
        string newText = "";
        bool spaceEncountered = false;
        for (int i = 0; i < text.Length; i++)
        {
            char dat = text[i];

            if (spaceEncountered)
            {
                newText += dat;
            }
            if (dat == ' ')
            {
                spaceEncountered = true;
            }
        }
        return newText;
    }

    //checks the line if it has # or [
    bool readLine(ref string textLine,ref StreamReader inReader, ref string joint)
    {
        textLine = inReader.ReadLine();

        if(textLine[0] == '#') // is a comment can ignore
        {
            return false;
        }
        if(textLine[0] == '[') //conext or chapter currently
        {
            if(textLine == "[Header]")
            {
                curMode = 0;
            }
            else if(textLine == "[SegmentNames&Hierarchy]")
            {
                curMode = currentHTRMode.SEGMENTHIERARCHY;
            }
            else if(textLine == "[BasePosition]")
            {
                curMode = currentHTRMode.BASE_POSITION;
            }
            else if(textLine == "[EndOfFile]")
            {
                curMode = currentHTRMode.END;
                done = true;
            }
            else
            {
                //read in [joingName] for processing
                joint = "";
                curMode = currentHTRMode.FRAMING;
                for(int i = 1; i < textLine.Length-1; i++)
                {
                    joint += textLine[i];
                }
            }
            return false;
        }
        return true;
    }
}
