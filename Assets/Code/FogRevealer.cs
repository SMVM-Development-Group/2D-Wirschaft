using UnityEngine;

public class FogRevealer : MonoBehaviour
{
    public float radius = 5f;
    public bool isLocalPlayer = true;
    private GameObject maskObj;
    private static Texture2D maskCircleTexture;

    private void Start()
    {
        CreateMask();
        if (isLocalPlayer)
        {
            FogProjector.RegisterExploration(transform.position, radius);
        }
    }

    private void CreateMask()
    {
        if (!isLocalPlayer) return;

        maskObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        maskObj.name = "MaskIndicator";
        maskObj.transform.SetParent(transform);
        maskObj.transform.localPosition = Vector3.zero;
        maskObj.transform.localScale = new Vector3(radius * 2.5f, radius * 2.5f, 1);
        
        maskObj.layer = 31;
        
        Renderer rend = maskObj.GetComponent<Renderer>();
        Shader maskShader = Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        if (maskShader == null)
        {
            Debug.LogWarning("[FogRevealer] Kein geeigneter Shader für die Maske gefunden. Verwende Standardmaterial.");
            rend.material = new Material(Shader.Find("Sprites/Default"));
        }
        else
        {
            rend.material = new Material(maskShader);
        }

        rend.material.mainTexture = GetMaskCircleTexture();
        rend.material.color = Color.white;
        rend.material.renderQueue = 3000;
        
        Destroy(maskObj.GetComponent<MeshCollider>());
    }

    private Texture2D GetMaskCircleTexture()
    {
        if (maskCircleTexture != null) return maskCircleTexture;

        int size = 128;
        maskCircleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        maskCircleTexture.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radiusPx = size / 2f;
        float edgeWidth = 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float alpha = dist <= radiusPx ? 1f : 0f;
                if (dist > radiusPx - edgeWidth && dist <= radiusPx)
                {
                    alpha = 1f - ((dist - (radiusPx - edgeWidth)) / edgeWidth);
                }
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        maskCircleTexture.SetPixels(pixels);
        maskCircleTexture.Apply();
        return maskCircleTexture;
    }

    private void OnEnable()
    {
        // Fuer FogProjector.IsVisible() bekannt machen (lokale Revealer bestimmen die Sichtbarkeit)
        FogProjector.AddRevealer(this);

        if (maskObj != null) maskObj.SetActive(true);
        if (isLocalPlayer)
        {
            FogProjector.RegisterExploration(transform.position, radius);
        }
    }

    private void OnDisable()
    {
        FogProjector.RemoveRevealer(this);

        if (maskObj != null) maskObj.SetActive(false);
    }

    private void OnDestroy()
    {
        FogProjector.RemoveRevealer(this);
    }
}
