using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpatialTarget : MonoBehaviour , IBound
{
    private bool m_selected = false;
    [SerializeField] private Sprite m_idleSprite;
    [SerializeField] private Sprite m_selectedSprite;
    

    public void Select(bool _selected = true)
    {
        m_selected = _selected;
    }

    private SpriteRenderer m_renderer;

    // Start is called before the first frame update
    private void Awake()
    {
        Bounds = new Bounds((Vector2)transform.position,Vector2.one);
    }

    void Start()
    {
        m_renderer = GetComponent<SpriteRenderer>();
        m_renderer.sprite = m_idleSprite;
    }

    private bool m_currentState = false;

    // Update is called once per frame
    void Update()
    {
        if (m_selected != m_currentState)
        {
            m_currentState = m_selected;
            m_renderer.sprite = m_selected ? m_selectedSprite : m_idleSprite;
            m_renderer.color = m_selected ? Color.magenta : Color.white;
        }
    }

    public Bounds Bounds { get;  set; }
}