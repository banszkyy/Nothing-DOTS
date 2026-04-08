using System;
using System.Text;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

public class DiskDriveManager : Singleton<DiskDriveManager>, IUISetup<Entity>, IUICleanup
{
    [Header("UI")]

    [SerializeField, SaintsField.ReadOnly] UIDocument? ui = default;

    Pendrive selected = default;

    void Update()
    {
        if (ui == null || !ui.gameObject.activeSelf) return;

        if (UIManager.Instance.GrapESC())
        {
            UIManager.Instance.CloseUI(this);
            return;
        }
    }

    public void Setup(UIDocument ui, Entity entity)
    {
        gameObject.SetActive(true);
        this.ui = ui;
        RefreshUI(entity);
    }

    public void RefreshUI(Entity entity)
    {
        if (ui == null || !ui.gameObject.activeSelf) return;

        selected = ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<Pendrive>(entity);

        Label labelHex = ui.rootVisualElement.Q<Label>("label-hex");
        Label labelAscii = ui.rootVisualElement.Q<Label>("label-ascii");

        StringBuilder builderHex = new();
        StringBuilder builderAscii = new();

        int until = 0;
        for (int i = selected.Span.Length - 1; i >= 0; i--)
        {
            if (selected.Span[i] != 0)
            {
                until = i + 1;
                break;
            }
        }

        for (int i = 0; i <= until; i++)
        {
            if (i > 0) builderHex.Append(' ');
            builderHex.Append(Convert.ToString(selected.Span[i], 16).PadLeft('0'));

            builderAscii.Append((char)selected.Span[i] switch
            {
                '\0' or '\b'
                    => '.',
                '\n' or '\r' or '\t'
                    => ' ',
                _ => (char)selected.Span[i],
            });
        }

        labelHex.text = builderHex.ToString();
        labelAscii.text = builderAscii.ToString();
    }

    public void Cleanup(UIDocument ui)
    {
        selected = default;
        gameObject.SetActive(false);
    }
}
