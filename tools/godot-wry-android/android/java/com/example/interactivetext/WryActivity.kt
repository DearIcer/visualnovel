// Copyright 2020-2023 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0
// SPDX-License-Identifier: MIT
//
// 基于 wry 的 WryActivity 模板适配 Godot：继承 GodotApp，
// 将 wry 创建的 WebView 叠加到 Godot 渲染视图之上而不是替换内容视图。

package com.example.interactivetext

import android.annotation.SuppressLint
import android.os.Build
import android.os.Bundle
import android.view.View
import android.view.ViewGroup
import android.webkit.WebView
import android.widget.FrameLayout
import com.godot.game.GodotApp

/**
 * Godot + wry 的主 Activity 基类。
 *
 * wry 的 Android 后端在创建 WebView 时会调用 `activity.setContentView(webview)`，
 * 这里将其拦截为向现有内容视图叠加一层 WebView，避免替换掉 Godot 的渲染视图。
 *
 * 导出时的 AndroidManifest.xml 需将主 activity 指向本类的子类（或本类的具体实现）。
 */
open class WryActivity : GodotApp() {
    private var webView: RustWebView? = null

    /** 由 wry Rust 侧调用，记录创建出来的 WebView。 */
    fun setWebView(webView: RustWebView) {
        this.webView = webView
        onWebViewCreate(webView)
    }

    open fun onWebViewCreate(webView: WebView) { }

    /** 由 wry Rust 侧调用，按名称查找应用内的类。 */
    fun getAppClass(name: String): Class<*> {
        return Class.forName(name)
    }

    /** 系统 WebView 版本，由 wry Rust 侧调用。 */
    val version: String
        @SuppressLint("WebViewApiAvailability")
        get() {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                return WebView.getCurrentWebViewPackage()?.versionName ?: ""
            }
            return ""
        }

    override fun setContentView(view: View) {
        if (view is RustWebView) {
            // 来自 wry：将 WebView 全屏叠加在 Godot 内容之上
            val content = findViewById<FrameLayout>(android.R.id.content)
            content.addView(
                view,
                ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.MATCH_PARENT,
                ),
            )
        } else {
            super.setContentView(view)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // 必须在 onCreate 内调用：wry 此时创建 RustWebChromeClient，
        // 其内部 registerForActivityResult 不允许在 STARTED 之后注册。
        nativeWrySetup(this)
    }

    override fun onDestroy() {
        super.onDestroy()
        onActivityDestroy(this)
    }

    companion object {
        init {
            System.loadLibrary("godot_wry")
        }

        /** 初始化 wry 的主线程消息管道，实现在 Rust 侧（libgodot_wry.so）。 */
        @JvmStatic
        private external fun nativeWrySetup(activity: WryActivity)

        /** wry 销毁回调，符号由 wry::android_binding! 生成。 */
        @JvmStatic
        private external fun onActivityDestroy(activity: Any?)
    }
}
