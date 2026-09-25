using System;
using UnityEngine;

// Inspector上の表示ラベルだけを日本語に差し替えるための属性。
// UnityのInspectorNameAttributeはenum値専用でフィールドラベルには効かないため、
// 表示側はAssets/BillGenerate/Editor/JpLabelDrawer.csのCustomPropertyDrawerで処理する。
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class JpLabelAttribute : PropertyAttribute
{
    public readonly string Label;

    public JpLabelAttribute(string label)
    {
        Label = label;
    }
}
