package com.videocamclient

import android.widget.FrameLayout
import android.widget.ImageView
import android.view.ViewGroup
import android.view.TextureView
import android.view.Gravity
import com.facebook.react.uimanager.SimpleViewManager
import com.facebook.react.uimanager.ThemedReactContext

class CameraPreviewManager : SimpleViewManager<FrameLayout>() {
    companion object {
        const val REACT_CLASS = "CameraPreview"
        @Volatile
        var currentTextureView: TextureView? = null
    }

    override fun getName(): String = REACT_CLASS

    override fun createViewInstance(reactContext: ThemedReactContext): FrameLayout {
        val container = FrameLayout(reactContext)

        val placeholder = ImageView(reactContext).apply {
            setImageResource(android.R.drawable.ic_menu_camera)
            scaleType = ImageView.ScaleType.CENTER_CROP
            layoutParams = FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT
            ).apply { gravity = Gravity.CENTER }
            setBackgroundColor(0xFF000000.toInt())
        }

        val texture = TextureView(reactContext).apply {
            layoutParams = FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT
            )
        }

        currentTextureView = texture
        container.addView(placeholder)
        container.addView(texture)

        return container
    }

    override fun onDropViewInstance(view: FrameLayout) {
        super.onDropViewInstance(view)
        currentTextureView = null
    }
}
