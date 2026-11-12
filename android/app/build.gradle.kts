import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
    id("org.jetbrains.kotlin.plugin.serialization")
}

// 发布签名：android/签名/keystore.properties（不存在时用调试签名）
val signProps = Properties().apply {
    val f = rootProject.file("签名/keystore.properties")
    if (f.exists()) f.inputStream().use { load(it) }
}

android {
    namespace = "com.weishu.helper"
    compileSdk = 35
    buildToolsVersion = "35.0.0"

    defaultConfig {
        applicationId = "com.weishu.helper"
        minSdk = 26          // 悬浮窗 TYPE_APPLICATION_OVERLAY 需要 Android 8.0+
        targetSdk = 35
        versionCode = 1
        versionName = "1.1.0"
    }

    signingConfigs {
        if (signProps.isNotEmpty()) {
            create("release") {
                storeFile = rootProject.file("签名/" + signProps.getProperty("storeFile"))
                storePassword = signProps.getProperty("storePassword")
                keyAlias = signProps.getProperty("keyAlias")
                keyPassword = signProps.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            signingConfig = signingConfigs.findByName("release") ?: signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { compose = true }

    // 规程数据和图片直接取桌面版的「数据」文件夹（工具/更新数据.py 更新后重新打包即可）
    sourceSets["main"].assets.srcDir(layout.buildDirectory.dir("gameAssets"))
}

val copyGameData by tasks.registering(Sync::class) {
    from(rootProject.file("../数据")) {
        exclude("图片/自定义/**")
        into("数据")
    }
    into(layout.buildDirectory.dir("gameAssets"))
}
tasks.named("preBuild") { dependsOn(copyGameData) }

dependencies {
    implementation(platform("androidx.compose:compose-bom:2024.12.01"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.lifecycle:lifecycle-service:2.8.7")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.7")
    implementation("androidx.savedstate:savedstate-ktx:1.2.1")
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.7.3")
}
