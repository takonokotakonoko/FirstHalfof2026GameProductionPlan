using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(JpLabelAttribute))]
public class JpLabelDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        JpLabelAttribute jpLabel = (JpLabelAttribute)attribute;
        EditorGUI.PropertyField(position, property, new GUIContent(jpLabel.Label), true);
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUI.GetPropertyHeight(property, label, true);
    }
}
