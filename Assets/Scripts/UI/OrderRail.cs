using System.Collections.Generic;
using UnityEngine;

public class OrderRail : MonoBehaviour
{
    [Header("Tham chiếu")]
    [SerializeField] private RectTransform ticketArea;
    [SerializeField] private OrderTicket ticketPrefab;
    [SerializeField] private Sprite[] foodSprites;

    [Header("Chạy & xếp hàng")]
    [SerializeField] private float spawnInterval = 6f;
    [SerializeField] private float moveSpeed = 300f;
    [SerializeField] private float gap = 24f;
    [SerializeField] private float padding = 24f;

    [Header("Kiên nhẫn")]
    [SerializeField] private float patienceDuration = 40f;

    // index 0 = thẻ đầu hàng (bên phải nhất), cuối list = thẻ mới nhất
    private readonly List<OrderTicket> _tickets = new List<OrderTicket>();
    private float _timer;
    private bool _patiencePaused;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        _timer = spawnInterval;   // sinh thẻ đầu tiên ngay
    }

    // Update is called once per frame
    private void Update()
    {
        _timer += Time.deltaTime;
        if (_timer >= spawnInterval && HasRoomToSpawn())
        {
            _timer = 0f;
            Spawn();
        }
        MoveTickets();
    }

    private bool HasRoomToSpawn()
    {
        if (_tickets.Count == 0) return true;
        // thẻ cuối đã vào đủ xa bên trong → còn chỗ cho thẻ mới phía sau
        OrderTicket last = _tickets[_tickets.Count - 1];
        return last.Rect.anchoredPosition.x - gap - last.Rect.rect.width >= 0f;
    }
    private void Spawn()
    {
        OrderTicket t = Instantiate(ticketPrefab, ticketArea);
        float w = t.Rect.rect.width;
        t.Rect.anchoredPosition = new Vector2(-w, 0f);   // bắt đầu ngoài mép trái

        Sprite food = (foodSprites != null && foodSprites.Length > 0)
            ? foodSprites[Random.Range(0, foodSprites.Length)]
            : null;
        t.Setup(food, patienceDuration);
        t.PatiencePaused = _patiencePaused;
        t.Removed += OnTicketRemoved;

        _tickets.Add(t);
    }
    private void MoveTickets()
    {
        float areaWidth = ticketArea.rect.width;
        float limitRight = areaWidth - padding;   // mép phải mà thẻ đầu hàng được tới

        for (int i = 0; i < _tickets.Count; i++)
        {
            OrderTicket t = _tickets[i];
            float w = t.Rect.rect.width;
            Vector2 pos = t.Rect.anchoredPosition;

            // vị trí đích: chạm mép phải, hoặc đứng sau thẻ phía trước
            float target = limitRight - w;
            if (i > 0) target = Mathf.Min(target, _tickets[i - 1].Rect.anchoredPosition.x - gap - w);

            if (pos.x < target)   // chỉ chạy sang phải, không bao giờ lùi
            {
                pos.x = Mathf.Min(pos.x + moveSpeed * Time.deltaTime, target);
                t.Rect.anchoredPosition = pos;
            }
            if (!t.PatienceStarted && t.Rect.anchoredPosition.x >= 0f) t.PatienceStarted = true;
        }
    }
    private void OnTicketRemoved(OrderTicket t)
    {
        _tickets.Remove(t);   // các thẻ phía sau sẽ tự chạy lên lấp chỗ
    }

    /// <summary>KitchenScreen gọi khi mở / đóng màn vẽ.</summary>
    public void SetPatiencePaused(bool paused)
    {
        _patiencePaused = paused;
        foreach (OrderTicket t in _tickets) t.PatiencePaused = paused;
    }
}
