using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor
{
    [InitializeOnLoad]
    internal static class StacklineLocalMcpConnect
    {
        static StacklineLocalMcpConnect()
        {
            if (Application.dataPath == "/Users/liuzhenjia/Documents/Codex/2026-08-10/ni/outputs/WukongEnvironment_NoCoaster/Assets")
                EditorApplication.update += ConnectWhenReady;
        }

        private static async void ConnectWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= ConnectWhenReady;
            try
            {
                string url = HttpEndpointUtility.GetBaseUrl().TrimEnd('/');
                if (url != "http://127.0.0.1:8080" && url != "http://localhost:8080")
                {
                    Debug.LogWarning("STACKLINE_MCP_SKIPPED: configured endpoint is not the existing local server.");
                    return;
                }
                if (!MCPServiceLocator.TransportManager.IsRunning(TransportMode.Http))
                    await MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http);
                Debug.Log("STACKLINE_MCP_CONNECTED: local client only, shared server unchanged.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("STACKLINE_MCP_CONNECT_FAILED: " + exception.Message);
            }
        }
    }
}
