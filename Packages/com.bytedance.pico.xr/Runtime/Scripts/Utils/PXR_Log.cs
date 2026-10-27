/*******************************************************************************
Copyright © 2015-2022 PICO Technology Co., Ltd.All rights reserved.  

NOTICE：All information contained herein is, and remains the property of 
PICO Technology Co., Ltd. The intellectual and technical concepts 
contained herein are proprietary to PICO Technology Co., Ltd. and may be 
covered by patents, patents in process, and are protected by trade secret or 
copyright law. Dissemination of this information or reproduction of this 
material is strictly forbidden unless prior written permission is obtained from
PICO Technology Co., Ltd. 
*******************************************************************************/

#if UNITY_ANDROID && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace ByteDance.PICO.XR
{
    public class PLog
    {
        public static LogLevel logLevel = LogLevel.LogWarn;

        public enum LogLevel
        {
            Unknow = 0,
            LogDefault = 1,
            LogVerbose= 2,
            LogDebug = 3,
            LogInfo = 4,
            LogWarn = 5,
            LogError = 6,
            LogFatal = 7,
        }

        public static void v(string tag, string message, bool showFrameCount = true)
        {
            if (LogLevel.LogVerbose >= logLevel)
            {
                Write(tag, message, showFrameCount);
            }
        }

        public static void d(string tag, string message, bool showFrameCount = true)
        {
            if (LogLevel.LogDebug >= logLevel)
            {
                Write(tag, message, showFrameCount);
            }
        }

        public static void i(string tag, string message, bool showFrameCount = true)
        {
            if (LogLevel.LogInfo >= logLevel)
            {
                Write(tag, message, showFrameCount);
            }
        }

        public static void w(string tag, string message, bool showFrameCount = true)
        {
            if (LogLevel.LogWarn >= logLevel)
            {
                Write(tag, message, showFrameCount);
            }
        }

        public static void e(string tag, string message, bool showFrameCount = true)
        {
            if (LogLevel.LogError >= logLevel)
            {
                Write(tag, message, showFrameCount);
            }
        }

        public static void f(string tag, string message, bool showFrameCount = true)
        {
            if (LogLevel.LogFatal >= logLevel)
            {
                Write(tag, message, showFrameCount);
            }
        }

        private static void Write(string tag, string message, bool showFrameCount)
        {
            // Android can request a log-level change from a Java broadcast thread where
            // Unity's frame counter is unavailable. Omitting it prevents an IL2CPP abort.
            Debug.Log(showFrameCount
                ? string.Format("{0}>>>>>>{1}", tag, message)
                : string.Format("{0} FrameID >>>>>>{1}", tag, message));
        }

        [System.Diagnostics.Conditional("DEBUG_CAMERA_PACK")]
        public static void CameraLog(string tag, string message)
        {
            Debug.Log($"[DEBUG_CAMERA_PACK] {tag}: {message}");
        }
    }
}
