using UnityEngine;

public class JoyveyorRunner : MonoBehaviour
{
    const int MaxSnapshotItems = 1024;

    public float[] itemPosXY;
    public uint[] itemIds;
    public ulong spawned;
    public ulong delivered;
    public ulong consumed;
    public int itemCount;
    public ulong tickCount;

    private IntPtr world = IntPtr.Zero;

    void OnEnable()
    {
        world = JoyveyorBridge.jv_world_create();
        PlaceDemoLevel();
    }

    void PlaceDemoLevel()
    {
        Check(JoyveyorBridge.jv_place_source(world, 0, 0), "source (0,0)");
        Check(JoyveyorBridge.jv_place_belt(world, 1, 0, JoyveyorBridge.DirE, 5), "belt (1,0) dir E len 5");
        Check(JoyveyorBridge.jv_place_splitter(world, 6, 0, JoyveyorBridge.DirE, JoyveyorBridge.DirS), "splitter (6,0) outA=E outB=S");
        Check(JoyveyorBridge.jv_place_belt(world, 7, 0, JoyveyorBridge.DirE, 4), "belt (7,0) dir E len 4");
        Check(JoyveyorBridge.jv_place_belt(world, 6, 1, JoyveyorBridge.DirS, 4), "belt (6,1) dir S len 4");
        Check(JoyveyorBridge.jv_place_merger(world, 6, 5, JoyveyorBridge.DirN, JoyveyorBridge.DirW, JoyveyorBridge.DirE), "merger (6,5) inA=N inB=W out=E");
        Check(JoyveyorBridge.jv_place_belt(world, 7, 5, JoyveyorBridge.DirE, 4), "belt (7,5) dir E len 4");
        Check(JoyveyorBridge.jv_place_sink(world, 11, 5, 50), "sink (11,5) capacity 50");
    }

    static void Check(uint id, string what)
    {
        if (id == JoyveyorBridge.InvalidId)
            Debug.LogWarning("[Joyveyor] placement rejected: " + what);
    }

    void FixedUpdate()
    {
        if (world != IntPtr.Zero)
            JoyveyorBridge.jv_world_advance(world, Time.fixedDeltaTime);
    }

    void LateUpdate()
    {
        if (world == IntPtr.Zero) return;
        JoyveyorBridge.GetSnapshot(world, 1.0f, MaxSnapshotItems);
        itemPosXY = JoyveyorBridge.LastPosXY;
        itemIds = JoyveyorBridge.LastIds;
        spawned = JoyveyorBridge.jv_spawned(world);
        delivered = JoyveyorBridge.jv_delivered(world);
        consumed = JoyveyorBridge.jv_consumed(world);
        itemCount = JoyveyorBridge.jv_item_count(world);
        tickCount = JoyveyorBridge.jv_world_tick_count(world);
    }

    void OnDestroy()
    {
        if (world != IntPtr.Zero)
        {
            JoyveyorBridge.jv_world_destroy(world);
            world = IntPtr.Zero;
        }
    }
}
