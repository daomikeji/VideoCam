package com.videocamclient

import android.opengl.EGL14
import android.opengl.EGLSurface
import android.view.Surface

class WindowSurface(private val eglCore: EglCore, surface: Surface, private val releaseSurface: Boolean = false) {
    private val eglSurface: EGLSurface = eglCore.createWindowSurface(surface)

    fun makeCurrent() {
        eglCore.makeCurrent(eglSurface)
    }

    fun swapBuffers(): Boolean = eglCore.swapBuffers(eglSurface)

    fun release() {
        try {
            // surface destruction handled by EGL implementation when context is released
        } catch (_: Exception) {}
    }
}
