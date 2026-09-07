using UnityEngine;
using UnityEngine.Rendering;

namespace Wukong.StacklineClassic
{
    /// <summary>Scene-owned override: the generated PICO scene also uses its pipeline in Play Mode.</summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class StacklinePicoRenderSettings : MonoBehaviour
    {
        [SerializeField] private RenderPipelineAsset pipeline;
        private RenderPipelineAsset previousPipeline;
        private bool applied;

        public RenderPipelineAsset Pipeline => pipeline;

        public void Configure(RenderPipelineAsset value)
        {
            pipeline = value;
        }

        private void Awake()
        {
            if (pipeline == null)
            {
                Debug.LogError("Stackline PICO render pipeline is missing.", this);
                return;
            }
            previousPipeline = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = pipeline;
            applied = true;
            Debug.Log("STACKLINE_PICO_CONFIG pipeline=" + pipeline.name +
                      "; graphics=" + SystemInfo.graphicsDeviceType +
                      "; device=" + SystemInfo.deviceModel +
                      "; unity=" + Application.unityVersion);
        }

        private void OnDestroy()
        {
            // Play Mode must not leave the desktop authoring environment on the mobile pipeline.
            if (applied && QualitySettings.renderPipeline == pipeline)
                QualitySettings.renderPipeline = previousPipeline;
        }
    }
}
