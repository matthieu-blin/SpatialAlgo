using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using UnityEngine.WSA;
using Random = UnityEngine.Random;

public class SpatialSearch : MonoBehaviour
{
    [SerializeField] private BoundsInt m_area;
    [SerializeField] private Tilemap m_tilemap;


    [SerializeField] private GameObject m_prefab;

    [SerializeField] private int m_numberOfInstance = 100;
    [SerializeField] private Text m_timeLabel;
    [SerializeField] private Toggle m_knnRangeToggle;
    [SerializeField] private Slider m_Slider;
    [SerializeField] private Dropdown m_Selection;


    private List<SpatialTarget> m_targets = new List<SpatialTarget>();

    // Start is called before the first frame update
    void Start()
    {
        Bounds b = new Bounds(Vector3.back, Vector3.one);
        Bounds a = new Bounds(new Vector3(-6, 5, 0), Vector3.one);
        b.Encapsulate(a);
        SortedSet<int> pos = new SortedSet<int>();
        for (int i = 0; i < m_numberOfInstance; ++i)
        {
            int x = Random.Range(m_area.xMin, m_area.xMax);
            int y = Random.Range(m_area.yMin, m_area.yMax);
            int index = (x - m_area.xMin) * m_area.size.x + (y - m_area.yMin);
            if (pos.Contains(index))
                continue;
            pos.Add(index);
            m_targets.Insert(0,
                GameObject.Instantiate(m_prefab,
                    m_tilemap.CellToWorld(new Vector3Int(x, y, 0)) + m_tilemap.cellSize / 2 + Vector3.back * 0.1f,
                    Quaternion.identity).GetComponent<SpatialTarget>()
            );
            m_targets.Sort((t1, t2) =>
                t1.transform.position.sqrMagnitude < t2.transform.position.sqrMagnitude ? -1 : 1);
        }

        m_knnRangeToggle.onValueChanged.AddListener((v) => m_SelectA = true);

        rt = new RTree<SpatialTarget>(9);
        // rt.StupidBulkLoad(m_targets);
        foreach (var tgt in m_targets)
        {
            rt.Insert(tgt);
        }
    }

    private RTree<SpatialTarget> rt;


    private void OnDestroy()
    {
        foreach (var target in m_targets)
        {
            DestroyImmediate(target);
        }

        m_targets.Clear();
    }

    [SerializeField] private GameObject m_selectedTile;

    private Vector3Int m_selectedPointA = new Vector3Int();
    private Vector3Int m_selectedPointB = new Vector3Int();
    private bool m_SelectA = true;


    // Update is called once per frame
    void Update()
    {
        if (EventSystem.current.IsPointerOverGameObject()) // is the touch on the GUI
        {
            // GUI Action
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            Vector3 mousePos = Input.mousePosition;
            mousePos.z = -Camera.main.transform.position.z;
            Vector3 worldPoint = Camera.main.ScreenToWorldPoint(mousePos);
            Vector3Int point = m_tilemap.WorldToCell(worldPoint);
            if (point.x >= m_area.xMin && point.x < m_area.xMax &&
                point.y >= m_area.yMin && point.y < m_area.yMax)
            {
                m_selectedPointB = point;
                if (m_SelectA)
                {
                    m_selectedPointA = point;
                }

                m_SelectA = !m_SelectA || !m_knnRangeToggle.isOn;

                UpdateSelection();
            }
        }
    }

    public void OnDrawGizmos()
    {
        if (rt != null && m_Selection.value == 1)
        {
            Action<IBound> f = null;
            f = b =>
            {
                Node n = b as Node;
                if (n != null)
                {
                    foreach (var child in n.Children)
                    {
                        f(child);
                    }

                    switch (n.Height)
                    {
                        case 0:
                            Gizmos.color = Color.red;
                            break;
                        case 1:
                            Gizmos.color = Color.blue;
                            break;
                        case 2:
                            Gizmos.color = Color.white;
                            break;
                        case 3:
                            Gizmos.color = Color.green;
                            break;
                        case 4:
                            Gizmos.color = Color.magenta;
                            break;
                        case 5:
                            Gizmos.color = Color.cyan;
                            break;
                        case 6:
                            Gizmos.color = Color.yellow;
                            break;
                        case 7:
                            Gizmos.color = Color.gray;
                            break;
                        case 8:
                            Gizmos.color = Color.black;
                            break;
                    }

                    if (rt.Root.Height - m_Slider.value != n.Height)
                        Gizmos.color = Color.clear;

                    Gizmos.DrawWireCube(b.Bounds.center, b.Bounds.size);
                }
            };
            m_Slider.maxValue = rt.Root.Height;

            f(rt.Root);
        }
    }

    private List<GameObject> selectedTiles = new List<GameObject>();

    void UpdateSelection()
    {
        //range
        var A = m_selectedPointA;
        var B = m_knnRangeToggle.isOn ? m_selectedPointB : m_selectedPointA;

        var selection = B - A;
        int nbCell = Math.Max(1, (Math.Abs(selection.x) + 1) * (Math.Abs(selection.y) + 1));

        while (selectedTiles.Count < nbCell)
        {
            selectedTiles.Add(GameObject.Instantiate(m_selectedTile));
        }

        selectedTiles.ForEach(g => g.SetActive(false));

        Vector3Int point = new Vector3Int();
        int index = 0;
        for (int x = Math.Min(A.x, B.x); x <= Math.Max(A.x, B.x); x++)
        {
            for (int y = Math.Min(A.y, B.y); y <= Math.Max(A.y, B.y); y++)
            {
                point.Set(x, y, 0);
                var g = selectedTiles[index];
                g.transform.position = m_tilemap.CellToWorld(point) + m_tilemap.cellSize / 2;
                g.SetActive(true);
                index++;
            }
        }
    }

    Stopwatch chrono = new Stopwatch();


    public void Search()
    {
        //reset 
        m_targets.AsParallel().ForAll(t => t.Select(false));
        IEnumerable<SpatialTarget> result = Enumerable.Empty<SpatialTarget>();
        chrono.Restart();
        if (!m_knnRangeToggle.isOn)
        {
            switch (m_Selection.value)
            {
                case 0:
                    result = KNN_Naive(4);
                    break;
                case 1:
                    result = KNN_RTree(4);
                    break;
            }
        }
        else
        {
            switch (m_Selection.value)
            {
                case 0:
                    result = Range_Naive();
                    break;
                case 1:
                    result = Range_RTree();
                    break;
            }
        }

        chrono.Stop();
        m_timeLabel.text = chrono.ElapsedTicks.ToString() + "  tick";
         
        result.AsParallel().ForAll(t => t.Select());
    }

    IEnumerable<SpatialTarget> KNN_Naive(int _n)
    {
        Vector3 pos = m_tilemap.CellToWorld(m_selectedPointA) + m_tilemap.cellSize / 22;
        List<SpatialTarget> result = new List<SpatialTarget>();
        for (int i = 0; i < _n; ++i)
        {
            SpatialTarget besttgt = null;
            float bestDist = float.MaxValue;
            foreach (var tgt in m_targets)
            {
                if (result.Contains(tgt))
                    continue;
                float dist = (tgt.transform.position - pos).magnitude;
                if (dist < bestDist)
                {
                    besttgt = tgt;
                    bestDist = dist;
                }
            }
            result.Add(besttgt);
            
        }
        return result;
    }

    IEnumerable<SpatialTarget> Range_Naive()
    {
        Vector3 posA = m_tilemap.CellToWorld(m_selectedPointA) + m_tilemap.cellSize / 2;
        Vector3 posB = m_tilemap.CellToWorld(m_selectedPointB) + m_tilemap.cellSize / 2;
        Vector3 max = Vector3.Max(posA, posB);
        Vector3 min = Vector3.Min(posA, posB);
        //cant use sorted list here since we will have some duplicate key
        List<SpatialTarget> result = new List<SpatialTarget>();
        foreach (var tgt in m_targets)
        {
            float x = tgt.transform.position.x;
            float y = tgt.transform.position.y;
            if (min.x <= x && x <= max.x && min.y <= y && y <= max.y)
                result.Add(tgt);
        }

        return result;
    }


    IEnumerable<SpatialTarget> KNN_RTree(int _n)
    {
        Vector3 pos = m_tilemap.CellToWorld(m_selectedPointA) + m_tilemap.cellSize / 2;
        return rt.Search(pos, _n);
    }

    IEnumerable<SpatialTarget> Range_RTree()
    {
        Vector3 posA = m_tilemap.CellToWorld(m_selectedPointA) + m_tilemap.cellSize / 2;
        Vector3 posB = m_tilemap.CellToWorld(m_selectedPointB) + m_tilemap.cellSize / 2;
        Vector3 center = (posB + posA) / 2;
        Vector3 sized = posA - posB;
        Vector3 size = new Vector3(Math.Abs(sized.x), Math.Abs(sized.y), 0);

        return rt.Search(new Bounds(center, size));
    }
}