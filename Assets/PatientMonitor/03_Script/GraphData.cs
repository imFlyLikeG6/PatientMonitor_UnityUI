using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GraphData", menuName = "Newbase/GraphData")]
public class GraphData : ScriptableObject
{
    [System.Serializable]
    public class Data
    {
        public float position;
        public float value;
        public float random_position = 0;
        public float random_value = 0;
    }

    public List<Data> data = new List<Data>();
    public float interval_time = 0.54f;
    public List<float> alert_position;
    public int wave_count = 1;
    public float regular_random_value;
}
