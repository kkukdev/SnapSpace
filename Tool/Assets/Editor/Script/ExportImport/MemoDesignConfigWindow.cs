using UnityEditor;
using UnityEngine;
using ObjDropWatcher.ExportImport;

public class MemoDesignConfigWindow : EditorWindow
{
    private Vector2 _scrollPosition;
    
    [MenuItem("Tools/Memo Design Config")]
    public static void Open()
    {
        var w = GetWindow<MemoDesignConfigWindow>("Memo Design Config");
        w.minSize = new Vector2(400, 600);
        w.Show();
    }
    
    void OnGUI()
    {
        MemoDesignConfig currentDesignConfig = MemoUtils.GetDesignConfig();
        if (currentDesignConfig == null)
        {
            EditorGUILayout.HelpBox("메모 디자인 설정을 불러올 수 없습니다.", MessageType.Warning);
            return;
        }
        
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Memo Design Configuration", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        EditorGUI.BeginChangeCheck();
        
        // 패널 높이 제어 (무조건 활성화)
        EditorGUILayout.LabelField("Panel Height Control", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        float fixedWorldY = EditorGUILayout.FloatField("Fixed Panel World Y", currentDesignConfig.fixedPanelWorldY);
        EditorGUILayout.HelpBox("모든 메모 패널이 지정한 월드 Y 높이에 고정됩니다.", MessageType.Info);
        EditorGUI.indentLevel--;
        
        // 마커 설정
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Marker Settings", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        float markerRadius = EditorGUILayout.FloatField("Marker Radius", currentDesignConfig.markerRadius);
        Color markerColor = EditorGUILayout.ColorField("Marker Color", currentDesignConfig.markerColor);
        EditorGUI.indentLevel--;
        
        // 선 설정
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Line Settings", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        float lineHeight = EditorGUILayout.FloatField("Line Height", currentDesignConfig.lineHeight);
        float lineWidth = EditorGUILayout.FloatField("Line Width", currentDesignConfig.lineWidth);
        Color lineColor = EditorGUILayout.ColorField("Line Color", currentDesignConfig.lineColor);
        EditorGUI.indentLevel--;
        
        // 네모창 설정
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Panel Settings", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        float panelWidth = EditorGUILayout.FloatField("Panel Width", currentDesignConfig.panelWidth);
        float panelHeight = EditorGUILayout.FloatField("Panel Height", currentDesignConfig.panelHeight);
        Color panelBackgroundColor = EditorGUILayout.ColorField("Panel Background Color", currentDesignConfig.panelBackgroundColor);
        Color panelBorderColor = EditorGUILayout.ColorField("Panel Border Color", currentDesignConfig.panelBorderColor);
        float panelBorderWidth = EditorGUILayout.FloatField("Panel Border Width", currentDesignConfig.panelBorderWidth);
        float panelPadding = EditorGUILayout.FloatField("Panel Padding", currentDesignConfig.panelPadding);
        EditorGUI.indentLevel--;
        
        // 텍스트 설정
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Text Settings", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        int fontSize = EditorGUILayout.IntField("Font Size", currentDesignConfig.fontSize);
        float characterSize = EditorGUILayout.FloatField("Character Size", currentDesignConfig.characterSize);
        Color textColor = EditorGUILayout.ColorField("Text Color", currentDesignConfig.textColor);
        TextAnchor anchor = (TextAnchor)EditorGUILayout.EnumPopup("Text Anchor", currentDesignConfig.anchor);
        TextAlignment alignment = (TextAlignment)EditorGUILayout.EnumPopup("Text Alignment", currentDesignConfig.alignment);
        EditorGUI.indentLevel--;
        
        // 고급 설정
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Advanced Settings", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        int maxNameLength = EditorGUILayout.IntField("Max Name Length", currentDesignConfig.maxNameLength);
        EditorGUI.indentLevel--;
        
        bool designChanged = EditorGUI.EndChangeCheck();
        
        if (designChanged)
        {
            MemoDesignConfig updatedConfig = CloneMemoDesignConfig(currentDesignConfig);
            if (updatedConfig != null)
            {
                // 패널 높이 제어 (무조건 활성화)
                updatedConfig.lockPanelWorldY = true;
                updatedConfig.fixedPanelWorldY = fixedWorldY;
                
                // 마커 설정
                updatedConfig.markerRadius = markerRadius;
                updatedConfig.markerColor = markerColor;
                
                // 선 설정
                updatedConfig.lineHeight = lineHeight;
                updatedConfig.lineWidth = lineWidth;
                updatedConfig.lineColor = lineColor;
                
                // 네모창 설정
                updatedConfig.panelWidth = panelWidth;
                updatedConfig.panelHeight = panelHeight;
                updatedConfig.panelBackgroundColor = panelBackgroundColor;
                updatedConfig.panelBorderColor = panelBorderColor;
                updatedConfig.panelBorderWidth = panelBorderWidth;
                updatedConfig.panelPadding = panelPadding;
                
                // 텍스트 설정
                updatedConfig.fontSize = fontSize;
                updatedConfig.characterSize = characterSize;
                updatedConfig.textColor = textColor;
                updatedConfig.anchor = anchor;
                updatedConfig.alignment = alignment;
                
                // 고급 설정
                updatedConfig.maxNameLength = maxNameLength;
                
                MemoUtils.SetDesignConfig(updatedConfig);
            }
        }
        
        EditorGUILayout.EndScrollView();
    }
    
    MemoDesignConfig CloneMemoDesignConfig(MemoDesignConfig source)
    {
        if (source == null)
            return null;
        
        return new MemoDesignConfig
        {
            markerRadius = source.markerRadius,
            markerColor = source.markerColor,
            lineHeight = source.lineHeight,
            lineWidth = source.lineWidth,
            lineColor = source.lineColor,
            panelWidth = source.panelWidth,
            panelHeight = source.panelHeight,
            panelBackgroundColor = source.panelBackgroundColor,
            panelBorderColor = source.panelBorderColor,
            panelBorderWidth = source.panelBorderWidth,
            panelPadding = source.panelPadding,
            fontSize = source.fontSize,
            characterSize = source.characterSize,
            textColor = source.textColor,
            anchor = source.anchor,
            alignment = source.alignment,
            maxNameLength = source.maxNameLength,
            lockPanelWorldY = source.lockPanelWorldY,
            fixedPanelWorldY = source.fixedPanelWorldY
        };
    }
}

