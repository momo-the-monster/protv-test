using ArchiTech.ProTV;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;

public class ActivateProTV : UdonSharpBehaviour
{
    [SerializeField] private TVManager _tv;

    [SerializeField] private GameObject ownerObject;
    
    [UdonSynced]
    private string _playersInTriggerString;
    public DataList playersInTrigger = new DataList();
    
    private bool _localPlayerInTrigger;
    
    private void Start()
    {
        _TvOwnerChange();
        _tv._RegisterListener(this);
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        // Add the player to the list
        if (Networking.LocalPlayer.IsOwner(gameObject))
        {
            playersInTrigger.Add(player.playerId);
            RequestSerialization();
        }
        
        // Don't do anything else if the player isn't local
        if (!player.isLocal) return;
     
        _localPlayerInTrigger = true;
        
        // turn on the tv
        _tv.gameObject.SetActive(true);
        
        // Catch the player up on what's happening
        _tv._ChangeSync(true);
    }
    
    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        // Remove player from synced list when running on list owner
        if (Networking.LocalPlayer.IsOwner(gameObject))
        {
            playersInTrigger.Remove(player.playerId);
            RequestSerialization();
            
            // Assign new tv owner if that player that just left was the owner
            if (player.IsOwner(_tv.gameObject))
            {
                AssignNextTVOwner();
            }
        }
        
        // All logic past here is for the local player only
        if (!player.isLocal) return;
        
        _localPlayerInTrigger = false;

        // The TV owner waits until a new owner is assigned before stopping the video if there are other people watching
        if (LocalPlayerIsTVOwner() && playersInTrigger.Count > 0) return;
        
        _tv._Stop();
        _tv.gameObject.SetActive(false);
    }

    private void AssignNextTVOwner()
    {
        if (playersInTrigger.Count == 0)
        {
            Debug.LogWarning("No players in trigger, can't assign new owner");
            return;
        }
        
        // Set the oldest player in the list to be the new owner
        Networking.SetOwner(VRCPlayerApi.GetPlayerById(playersInTrigger[0].Int), _tv.gameObject);
    }

    private bool LocalPlayerIsTVOwner()
    {
        return Networking.LocalPlayer.IsOwner(_tv.gameObject);
    }
    
    public void _TvOwnerChange()
    {
        ownerObject.SetActive(LocalPlayerIsTVOwner());
        
        // Handle the case where another player in the instance was made the owner, but they're not in the trigger zone
        if (LocalPlayerIsTVOwner() && !_localPlayerInTrigger)
        {
            AssignNextTVOwner();
        }

        // When the TV Owner leaves while others are still watching, they wait until this point to stop the video.
        // It will also run for all players not currently in the trigger, which is fine.
        if (!_localPlayerInTrigger)
        {
            _tv.gameObject.SetActive(false);
            _tv._Stop();
        }
    }
    
    // Resync when playback starts
    public void _TvPlay()
    {
        if(!LocalPlayerIsTVOwner())
            _tv._ReSync();
    }
    
    public override void OnPreSerialization()
    {
        if (VRCJson.TrySerializeToJson(playersInTrigger, JsonExportType.Minify, out DataToken result))
        {
            _playersInTriggerString = result.String;
        }
        else
        {
            Debug.LogError(result.ToString());
        }
    }

    public override void OnDeserialization()
    {
        if(VRCJson.TryDeserializeFromJson(_playersInTriggerString, out DataToken result))
        {
            playersInTrigger = result.DataList;
        }
        else
        {
            Debug.LogError(result.ToString());
        }
    }
}
