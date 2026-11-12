# 数据类靠 kotlinx.serialization 生成的序列化器读写 JSON，保留它们
-keepattributes *Annotation*, InnerClasses
-keep,includedescriptorclasses class com.weishu.helper.**$$serializer { *; }
-keepclassmembers class com.weishu.helper.** {
    *** Companion;
}
-keepclasseswithmembers class com.weishu.helper.** {
    kotlinx.serialization.KSerializer serializer(...);
}
