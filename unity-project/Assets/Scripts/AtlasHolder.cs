using UnityEngine;

// JoyVeyor v1.0 — Sprint 6: scene reference to the baked pixel-art atlas
// (Assets/Art/atlas.png). The scene reference is what makes the build
// pipeline include the atlas in the standalone player (it is referenced
// from code only, not from Resources/). Hands the texture to JVArt on
// scene load; JVArt falls back to plain white sprites if it is missing.
public class AtlasHolder : MonoBehaviour
{
    public Texture2D atlas;

    void Awake()
    {
        if (atlas != null) JVArt.SetAtlasTexture(atlas);
    }
}
