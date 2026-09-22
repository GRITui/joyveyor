using UnityEngine;

[RequireComponent(typeof(JoyveyorRunner))]
public class ItemRenderer : MonoBehaviour
{
    const int InitialPoolSize = 256;

    public Transform root;
    public float cellSize = 1f;
    // World units the item occupies (spec §3: 0.4 cell ≈ 24 screen px).
    // The crate sprite is 16px @ 100 px/unit = 0.16 units, so the pool
    // scale is itemScale / 0.16 (= 2.5 at 0.4).
    public float itemScale = 0.4f;

    private JoyveyorRunner runner;
    private Sprite crateSprite;
    private GameObject[] pool;
    private int poolSize;

    void OnEnable()
    {
        runner = GetComponent<JoyveyorRunner>();
        if (root == null) root = transform;
        // v1.0 is a SINGLE item type — render the crate (JVArt; falls back
        // to a white 1x1 if the atlas is missing from a checkout).
        JVArt.EnsureLoaded();
        crateSprite = JVArt.Crate;
        poolSize = InitialPoolSize;
        pool = new GameObject[poolSize];
        for (int i = 0; i < poolSize; ++i)
            pool[i] = CreatePoolObject();
    }

    GameObject CreatePoolObject()
    {
        var go = new GameObject("item");
        go.transform.SetParent(root, false);
        go.transform.localScale = Vector3.one * (itemScale / 0.16f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = crateSprite;
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
