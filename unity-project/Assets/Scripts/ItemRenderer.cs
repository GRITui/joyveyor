using UnityEngine;

[RequireComponent(typeof(JoyveyorRunner))]
public class ItemRenderer : MonoBehaviour
{
    const int InitialPoolSize = 256;

    public Transform root;
    public float cellSize = 1f;
    public float itemScale = 0.2f;

    private JoyveyorRunner runner;
    private Sprite whiteSprite;
    private GameObject[] pool;
    private int poolSize;

    void OnEnable()
    {
        runner = GetComponent<JoyveyorRunner>();
        if (root == null) root = transform;
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        poolSize = InitialPoolSize;
        pool = new GameObject[poolSize];
        for (int i = 0; i < poolSize; ++i)
            pool[i] = CreatePoolObject();
    }

    GameObject CreatePoolObject()
    {
        var go = new GameObject("item");
        go.transform.SetParent(root, false);
        go.transform.localScale = Vector3.one * itemScale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = whiteSprite;
        go.SetActive(false);
        return go;
    }

    void GrowPool(int needed)
    {
        int newSize = poolSize;
        while (newSize < needed) newSize *= 2;
        var grown = new GameObject[newSize];
        for (int i = 0; i < poolSize; ++i) grown[i] = pool[i];
        for (int i = poolSize; i < newSize; ++i) grown[i] = CreatePoolObject();
        pool = grown;
        poolSize = newSize;
    }

    void LateUpdate()
    {
        if (runner == null) return;
        var pos = runner.itemPosXY;
        if (pos == null) return;
        int count = Mathf.Min(runner.itemCount, pos.Length / 2);
        if (count > poolSize) GrowPool(count);
        for (int i = 0; i < count; ++i)
        {
            float x = pos[2 * i];
            float y = pos[2 * i + 1];
            var go = pool[i];
            go.transform.position = new Vector3(x, -y, 0f) * cellSize;
            go.SetActive(true);
        }
        for (int i = count; i < poolSize; ++i)
            pool[i].SetActive(false);
    }
}
