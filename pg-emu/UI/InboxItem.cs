using Godot;

namespace PGEmu.UI;

// BASE FOR ALL OTHER INBOX ITEMS, reusable
public partial class InboxItem : Panel
{
    public virtual void Setup(object data)
    {
        // Base method — override 
    }

    public virtual void OnAccept() { }
    public virtual void OnDecline() { }
}