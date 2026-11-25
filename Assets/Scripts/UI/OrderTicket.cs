using NUnit.Framework;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class OrderTicket : MonoBehaviour
{
    [SerializeField] private Image patienceFill;
    [SerializeField] private Image foodImage;

    [SerializeField] private float patienceDuration = 40f;
    [SerializeField, UnityEngine.Range(0f, 1f)] private float yellowBelow = 0.5f;
    [SerializeField, UnityEngine.Range(0f, 1f)] private float redBelow = 0.2f;
    [SerializeField] private Color green = new Color(0.10f, 0.90f, 0.10f);
    [SerializeField] private Color yellow = new Color(1.00f, 0.85f, 0.00f);
    [SerializeField] private Color red = new Color(0.85f, 0.05f, 0.05f);
    [SerializeField] private float fadeDuration = 0.3f;

    public RectTransform Rect { get; private set;  }
    public bool IsLeaving { get; private set; }
    public bool PatiencePaused { get; set; }
    public bool PatienceStarted { get; set; }   // OrderRail bật khi thẻ đã vào hẳn màn hình
    public float Patience01 => patienceDuration > 0f ? _remaining / patienceDuration : 0f;
    /// <summary>Gọi khi thẻ đã mờ hẳn, ngay trước khi bị xóa.</summary>
    public event Action<OrderTicket> Removed;

    private float _remaining;
    private CanvasGroup _group;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        Rect = (RectTransform)transform;
        _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        _remaining = patienceDuration;
        UpdateBar();
    }
    /// <summary>OrderRail gọi ngay sau khi sinh thẻ.</summary>
    public void Setup(Sprite food, float patience)
    {
        if (food!=null && foodImage!=null)
            foodImage.sprite = food;
        _remaining = patience;
        UpdateBar();
    }
    // Update is called once per frame
    private void Update()
    {
        if (IsLeaving || PatiencePaused || !PatienceStarted) return;
        _remaining -= Time.deltaTime;
        if (_remaining <= 0f)
        {
            _remaining = 0f;
            UpdateBar();
            RemoveOrder();          // hết kiên nhẫn → khách bỏ đi
            return;
        }
        UpdateBar();
    }

    private void UpdateBar()
    {
        if (patienceFill == null) return;
        float p = Patience01;
        patienceFill.fillAmount = p;
        patienceFill.color = p < redBelow ? red : (p < yellowBelow ? yellow : green);
    }
    /// <summary>Xóa thẻ (mờ dần). Sau này gọi thêm khi giao món xong.</summary>
    public void RemoveOrder()
    {
        if (IsLeaving) return;
        IsLeaving = true;
        StartCoroutine(FadeAndDestroy());
    }
    //can sua hieu ung cho nay
    private IEnumerator FadeAndDestroy()
    {
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            _group.alpha = 1f - Mathf.Clamp01(t / fadeDuration);
            yield return null;
        }
        Removed?.Invoke(this);
        Destroy(gameObject);
    }
}
