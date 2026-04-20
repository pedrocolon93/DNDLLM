// Assets/Scripts/UI/MapCameraController.cs
using UnityEngine;
using UnityEngine.UI;

namespace DnD.UI
{
    [RequireComponent(typeof(Camera))]
    public class MapCameraController : MonoBehaviour
    {
        public static MapCameraController Instance { get; private set; }

        [SerializeField] private int renderWidth  = 1024;
        [SerializeField] private int renderHeight = 1024;

        private RenderTexture renderTexture;
        private Camera mapCamera;

        private void Awake()
        {
            Instance = this;
            mapCamera = GetComponent<Camera>();

            renderTexture = new RenderTexture(renderWidth, renderHeight, 16, RenderTextureFormat.ARGB32);
            renderTexture.Create();
            mapCamera.targetTexture = renderTexture;

            // Find the RawImage in the scene named "MapDisplay" and assign texture
            var rawImages = FindObjectsByType<RawImage>(FindObjectsInactive.Include);
            foreach (var ri in rawImages)
            {
                if (ri.gameObject.name == "MapDisplay")
                {
                    ri.texture = renderTexture;
                    break;
                }
            }
        }

        private void OnDestroy()
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }
        }

        public Camera MapCamera => mapCamera;
    }
}
