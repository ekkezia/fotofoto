using UnityEngine;
using UnityEditor;

public class FindMissingScripts : EditorWindow
{
    [MenuItem("Tools/Find Missing Scripts In Scene")]
    public static void FindMissingScriptsInScene()
    {
        int count = 0;
        GameObject[] gos = GameObject.FindObjectsOfType<GameObject>();
        foreach (GameObject go in gos)
        {
            Component[] components = go.GetComponents<Component>();
            foreach (Component c in components)
            {
                if (c == null)
                {
                    Debug.LogWarning($"❌ Missing script found on GameObject: {GetFullPath(go)}", go);
                    count++;
                }
            }
        }

        Debug.Log($"✅ Done! Found {count} missing scripts total.");
    }

    static string GetFullPath(GameObject go)
    {
        return go.transform.parent == null ? go.name : GetFullPath(go.transform.parent.gameObject) + "/" + go.name;
    }
}
