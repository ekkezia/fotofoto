using System.Collections.Generic;
using Firebase;
using Firebase.Database;
using UnityEngine;

public class FirebaseManager : MonoBehaviour
{
    private DatabaseReference database;
    public string currentRoomId = "TEST";

    void Start()
    {
        // Initialize Firebase and cache the root database reference
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task =>
        {
            database = FirebaseDatabase.DefaultInstance.RootReference;
            Debug.Log("Firebase initialized!");
        });
    }

    public void UploadCapture(string base64Image)
    {
        var captureData = new Dictionary<string, object>
        {
            { "timestamp", ServerValue.Timestamp },
            { "imageData", base64Image }
        };

        database.Child("rooms").Child(currentRoomId).Child("captures").Push().SetValueAsync(captureData);
    }
}