using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class SelectionManager : MonoBehaviour
{
    private const float DragThreshold = 10f;
    private const float FormationSpacing = 1.5f;

    public static SelectionManager Instance { get; private set; }

    [Header("Selection Settings")]
    [SerializeField] private Color highlightColor = new Color(1f, 1f, 1f, 0.3f);
    [SerializeField] private Color dragFillColor = new Color(0.30f, 0.85f, 1f, 0.18f);
    [SerializeField] private Color dragBorderColor = new Color(0.30f, 0.85f, 1f, 0.95f);

    private readonly List<Soldier> selectedSoldiers = new List<Soldier>();
    private readonly List<BuildingInstance> selectedBuildings = new List<BuildingInstance>();
    private readonly List<GameObject> buildingHighlights = new List<GameObject>();

    private const float DoubleClickTime = 0.35f;
    private BuildingInstance lastClickedBuilding;
    private float lastClickTime;

    public IReadOnlyList<BuildingInstance> GetSelectedBuildings()
    {
        selectedBuildings.RemoveAll(b => b == null);
        return selectedBuildings;
    }

    private GameObject highlightObj;
    private LineRenderer observeLineRenderer;
    private Camera cam;
    private BuildingInfoPanel infoPanel;
    private Texture2D dragTexture;
    private Vector2 dragStartScreen;
    private bool isDraggingSelection;
    private bool startedOnUi;
    private ArmyCommandMode pendingCommandMode;
    private bool waitingForObserveSecondPoint;
    private Vector2 observeFirstPoint;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        cam = Camera.main;
        CreateHighlightObject();
        CreateObserveLineObject();
        CreateDragTexture();
        EnsureInfoPanel();
        EnsureDayNightManager();
        RefreshCommandUi();
    }

    private void EnsureInfoPanel()
    {
        infoPanel = BuildingInfoPanel.Instance;
        if (infoPanel == null)
        {
            GameObject panelGO = new GameObject("BuildingInfoPanel");
            infoPanel = panelGO.AddComponent<BuildingInfoPanel>();
            Debug.Log("[SelectionManager] BuildingInfoPanel auto-created.");
        }
    }

    private void EnsureDayNightManager()
    {
        if (DayNightManager.Instance == null)
        {
            GameObject go = new GameObject("DayNightManager");
            go.AddComponent<DayNightManager>();
            Debug.Log("[SelectionManager] DayNightManager auto-created.");
        }
    }

    private void CreateHighlightObject()
    {
        highlightObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        highlightObj.name = "SelectionHighlight";
        highlightObj.transform.localScale = Vector3.one;
        highlightObj.transform.position = new Vector3(0f, 0f, -0.19f);
        Destroy(highlightObj.GetComponent<MeshCollider>());

        Renderer rend = highlightObj.GetComponent<Renderer>();
        rend.material = new Material(Shader.Find("Sprites/Default"));
        rend.material.color = highlightColor;
        highlightObj.SetActive(false);
    }

    private void CreateObserveLineObject()
    {
        GameObject lineObject = new GameObject("ObserveGroupLine");
        observeLineRenderer = lineObject.AddComponent<LineRenderer>();
        observeLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        observeLineRenderer.positionCount = 2;
        observeLineRenderer.startWidth = 0.16f;
        observeLineRenderer.endWidth = 0.16f;
        observeLineRenderer.useWorldSpace = true;
        observeLineRenderer.startColor = new Color(0.35f, 0.85f, 1f, 0.85f);
        observeLineRenderer.endColor = observeLineRenderer.startColor;
        observeLineRenderer.sortingOrder = 6;
        observeLineRenderer.enabled = false;
    }

    private void CreateDragTexture()
    {
        dragTexture = new Texture2D(1, 1);
        dragTexture.SetPixel(0, 0, Color.white);
        dragTexture.Apply();
    }

    private void Update()
    {
        if (Mouse.current == null || cam == null)
        {
            return;
        }

        if (Player_UI.Instance != null && Player_UI.Instance.IsSubMenuOpen)
        {
            return;
        }

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            HandleRightClick();
        }

        UpdateHoverHighlight();
        HandleLeftMouseFlow();
    }

    private void HandleRightClick()
    {
        if (selectedSoldiers.Count == 0) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, -cam.transform.position.z));

        Collider2D[] hits = Physics2D.OverlapPointAll((Vector2)worldPos);
        BuildingInstance targetBuilding = null;
        Soldier targetSoldier = null;

        foreach (var hit in hits)
        {
            if (targetBuilding == null)
            {
                BuildingInstance b = hit.GetComponent<BuildingInstance>();
                if (b != null && !b.isLocal)
                    targetBuilding = b;
            }
            if (targetSoldier == null)
            {
                Soldier s = hit.GetComponent<Soldier>();
                if (s != null && s.IsHostileTo(selectedSoldiers[0]))
                    targetSoldier = s;
            }
            if (targetBuilding != null && targetSoldier != null) break;
        }

        if (targetBuilding != null)
        {
            foreach (var s in selectedSoldiers)
                if (s != null) s.IssueAttackBuildingOrder(targetBuilding);
            pendingCommandMode = ArmyCommandMode.None;
            RefreshCommandUi();
        }
        else if (targetSoldier != null)
        {
            foreach (var s in selectedSoldiers)
                if (s != null) s.IssueAttackOrder(targetSoldier);
            pendingCommandMode = ArmyCommandMode.None;
            RefreshCommandUi();
        }
        else
        {
            IssueFormationCommand((Vector2)worldPos, false);
            pendingCommandMode = ArmyCommandMode.None;
            RefreshCommandUi();
        }
    }

    public void BeginMoveCommand()
    {
        pendingCommandMode = ArmyCommandMode.Move;
        waitingForObserveSecondPoint = false;
        RefreshCommandUi("Klicke einen Landpunkt fur den Marschbefehl.");
    }

    public void BeginAttackCommand()
    {
        pendingCommandMode = ArmyCommandMode.AttackMove;
        waitingForObserveSecondPoint = false;
        RefreshCommandUi("Klicke einen Landpunkt fur Angriff auf dem Weg.");
    }

    public void BeginObserveCommand()
    {
        pendingCommandMode = ArmyCommandMode.Observe;
        waitingForObserveSecondPoint = false;
        RefreshCommandUi("Klicke den ersten Beobachtungspunkt.");
    }

    public void NotifySoldierDestroyed(Soldier soldier)
    {
        if (selectedSoldiers.Remove(soldier))
        {
            RefreshCommandUi();
        }
    }

    private void UpdateHoverHighlight()
    {
        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, -cam.transform.position.z));

        float snappedX = Mathf.Round(worldPos.x);
        float snappedY = Mathf.Round(worldPos.y);
        highlightObj.transform.position = new Vector3(snappedX, snappedY, -0.19f);
        highlightObj.SetActive(true);
    }

    private void HandleLeftMouseFlow()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            dragStartScreen = Mouse.current.position.ReadValue();
            startedOnUi = IsPointerOverUi();
            isDraggingSelection = false;
        }

        if (Mouse.current.leftButton.isPressed && !startedOnUi)
        {
            Vector2 currentMouse = Mouse.current.position.ReadValue();
            if ((currentMouse - dragStartScreen).sqrMagnitude >= DragThreshold * DragThreshold)
            {
                isDraggingSelection = true;
            }
        }

        if (!Mouse.current.leftButton.wasReleasedThisFrame)
        {
            return;
        }

        isDraggingSelection = false;

        if (startedOnUi)
        {
            startedOnUi = false;
            return;
        }

        Vector2 releaseScreen = Mouse.current.position.ReadValue();
        Vector2 worldRelease = cam.ScreenToWorldPoint(new Vector3(releaseScreen.x, releaseScreen.y, -cam.transform.position.z));

        if (pendingCommandMode != ArmyCommandMode.None && !isDraggingSelection)
        {
            if (TryHandlePendingCommand(worldRelease))
            {
                startedOnUi = false;
                return;
            }
        }

        if ((releaseScreen - dragStartScreen).sqrMagnitude >= DragThreshold * DragThreshold)
        {
            SelectSoldiersInRectangle(dragStartScreen, releaseScreen);
            startedOnUi = false;
            return;
        }

        HandleSingleClick(worldRelease);
        startedOnUi = false;
    }

    private bool TryHandlePendingCommand(Vector2 clickedWorld)
    {
        if (selectedSoldiers.Count == 0)
        {
            pendingCommandMode = ArmyCommandMode.None;
            waitingForObserveSecondPoint = false;
            RefreshCommandUi();
            return false;
        }

        if (pendingCommandMode == ArmyCommandMode.Observe)
        {
            if (!waitingForObserveSecondPoint)
            {
                observeFirstPoint = clickedWorld;
                waitingForObserveSecondPoint = true;
                RefreshCommandUi("Klicke jetzt den zweiten Beobachtungspunkt.");
                return true;
            }

            IssueObserveCommand(observeFirstPoint, clickedWorld);
            pendingCommandMode = ArmyCommandMode.None;
            waitingForObserveSecondPoint = false;
            RefreshCommandUi();
            return true;
        }

        if (pendingCommandMode == ArmyCommandMode.Move)
        {
            IssueFormationCommand(clickedWorld, false);
        }
        else if (pendingCommandMode == ArmyCommandMode.AttackMove)
        {
            IssueFormationCommand(clickedWorld, true);
        }

        pendingCommandMode = ArmyCommandMode.None;
        waitingForObserveSecondPoint = false;
        RefreshCommandUi();
        return true;
    }

    private void HandleSingleClick(Vector2 worldPos2D)
    {
        Collider2D[] hits = Physics2D.OverlapPointAll(worldPos2D);

        System.Array.Sort(hits, (a, b) =>
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            return (a.transform.position - (Vector3)worldPos2D).sqrMagnitude
                .CompareTo((b.transform.position - (Vector3)worldPos2D).sqrMagnitude);
        });

        Soldier clickedSoldier = null;
        BuildingInstance clickedBuilding = null;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            Soldier soldier = hit.GetComponent<Soldier>();
            if (soldier != null && soldier.IsOwnedByLocalPlayer)
            {
                clickedSoldier = soldier;
                break;
            }

            if (clickedBuilding == null)
            {
                clickedBuilding = hit.GetComponent<BuildingInstance>();
            }
        }

        if (clickedSoldier != null)
        {
            AudioManager.Instance?.PlaySelectSound();
            SelectSingleSoldier(clickedSoldier);
            ClearBuildingSelection();
            if (infoPanel != null && infoPanel.IsVisible)
            {
                infoPanel.Hide();
            }
            return;
        }

        if (clickedBuilding != null)
        {
            if (!clickedBuilding.isLocal) return;

            // Schiffe haben ihre eigene UI (ShipCargoUI), kein BuildingInfoPanel öffnen
            if (clickedBuilding.GetComponent<Ship>() != null)
            {
                if (infoPanel != null && infoPanel.IsVisible) infoPanel.Hide();
                return;
            }

            AudioManager.Instance?.PlaySelectSound();
            ClearSoldierSelection();

            float now = Time.realtimeSinceStartup;
            bool isDoubleClick = clickedBuilding.data != null &&
                lastClickedBuilding != null &&
                lastClickedBuilding.data != null &&
                clickedBuilding.data.buildingName == lastClickedBuilding.data.buildingName &&
                (now - lastClickTime) < DoubleClickTime;

            lastClickedBuilding = clickedBuilding;
            lastClickTime = now;

            if (isDoubleClick)
            {
                SelectAllOfType(clickedBuilding);
            }
            else
            {
                SelectSingleBuilding(clickedBuilding);
            }
            return;
        }

        ClearSoldierSelection();
        ClearBuildingSelection();
        if (infoPanel != null && infoPanel.IsVisible)
        {
            infoPanel.Hide();
        }
    }

    private void SelectSingleSoldier(Soldier soldier)
    {
        ClearSoldierSelection();
        selectedSoldiers.Add(soldier);
        soldier.SetSelected(true);
        RefreshCommandUi();
    }

    private void SelectSoldiersInRectangle(Vector2 screenStart, Vector2 screenEnd)
    {
        ClearSoldierSelection();

        Rect rect = GetScreenRect(screenStart, screenEnd);
        for (int i = 0; i < Soldier.ActiveSoldiers.Count; i++)
        {
            Soldier soldier = Soldier.ActiveSoldiers[i];
            if (soldier == null || !soldier.IsOwnedByLocalPlayer)
            {
                continue;
            }

            Vector3 screenPos = cam.WorldToScreenPoint(soldier.transform.position);
            if (screenPos.z < 0f)
            {
                continue;
            }

            if (rect.Contains(new Vector2(screenPos.x, screenPos.y)))
            {
                selectedSoldiers.Add(soldier);
                soldier.SetSelected(true);
            }
        }

        RefreshCommandUi();
    }

    private void ClearSoldierSelection()
    {
        for (int i = 0; i < selectedSoldiers.Count; i++)
        {
            if (selectedSoldiers[i] != null)
            {
                selectedSoldiers[i].SetSelected(false);
            }
        }

        selectedSoldiers.Clear();
        pendingCommandMode = ArmyCommandMode.None;
        waitingForObserveSecondPoint = false;
        if (observeLineRenderer != null)
        {
            observeLineRenderer.enabled = false;
        }
        RefreshCommandUi();
    }

    private void SelectSingleBuilding(BuildingInstance building)
    {
        ClearBuildingSelection();
        selectedBuildings.Add(building);
        UpdateBuildingHighlights();
        infoPanel?.Show(building);
    }

    private void SelectAllOfType(BuildingInstance building)
    {
        ClearBuildingSelection();
        BuildingInstance.AllBuildings.RemoveAll(b => b == null);

        string typeName = building.data.buildingName;
        string displayName = building.GetDisplayName();

        foreach (var b in BuildingInstance.AllBuildings)
        {
            if (b != null && b.isLocal && b.data != null && b.data.buildingName == typeName)
            {
                selectedBuildings.Add(b);
            }
        }

        UpdateBuildingHighlights();

        if (selectedBuildings.Count > 1)
        {
            NotificationManager.Instance?.Notify("multi_select",
                $"{selectedBuildings.Count}× {displayName} ausgewählt", 3f);
        }

        if (selectedBuildings.Count > 0)
        {
            infoPanel?.Show(selectedBuildings[0]);
        }
    }

    public void ClearBuildingSelection()
    {
        selectedBuildings.Clear();
        ClearBuildingHighlights();
    }

    private void UpdateBuildingHighlights()
    {
        ClearBuildingHighlights();
        if (selectedBuildings.Count <= 1) return;
        foreach (var b in selectedBuildings)
        {
            if (b == null) continue;
            GameObject highlight = GameObject.CreatePrimitive(PrimitiveType.Quad);
            highlight.name = "BuildingSelectionHighlight";
            Destroy(highlight.GetComponent<MeshCollider>());

            highlight.transform.SetParent(b.transform, false);
            highlight.transform.localPosition = new Vector3(0f, 0f, 0.15f);
            highlight.transform.localScale = Vector3.one;

            Renderer rend = highlight.GetComponent<Renderer>();
            rend.material = new Material(Shader.Find("Sprites/Default"));
            rend.material.color = new Color(1f, 0.84f, 0f, 0.35f);
            rend.sortingOrder = 1;
            buildingHighlights.Add(highlight);
        }
    }

    private void ClearBuildingHighlights()
    {
        foreach (var h in buildingHighlights)
        {
            if (h != null) Destroy(h);
        }
        buildingHighlights.Clear();
    }

    private void IssueFormationCommand(Vector2 targetPoint, bool attackMove)
    {
        List<Vector2> formationOffsets = BuildFormationOffsets(selectedSoldiers.Count);
        for (int i = 0; i < selectedSoldiers.Count; i++)
        {
            Soldier soldier = selectedSoldiers[i];
            if (soldier == null)
            {
                continue;
            }

            Vector2 destination = targetPoint + formationOffsets[i];
            if (attackMove)
            {
                soldier.IssueAttackMoveOrder(destination);
            }
            else
            {
                soldier.IssueMoveOrder(destination);
            }
        }
    }

    private void IssueObserveCommand(Vector2 pointA, Vector2 pointB)
    {
        List<Vector2> formationOffsets = BuildFormationOffsets(selectedSoldiers.Count);
        for (int i = 0; i < selectedSoldiers.Count; i++)
        {
            Soldier soldier = selectedSoldiers[i];
            if (soldier == null)
            {
                continue;
            }

            Vector2 offset = formationOffsets[i] * 0.45f;
            soldier.IssueObserveOrder(pointA + offset, pointB + offset);
        }

        if (observeLineRenderer != null)
        {
            observeLineRenderer.enabled = true;
            observeLineRenderer.SetPosition(0, new Vector3(pointA.x, pointA.y, -0.06f));
            observeLineRenderer.SetPosition(1, new Vector3(pointB.x, pointB.y, -0.06f));
        }
    }

    private static List<Vector2> BuildFormationOffsets(int count)
    {
        List<Vector2> offsets = new List<Vector2>(count);
        if (count <= 0)
        {
            return offsets;
        }

        int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = Mathf.CeilToInt(count / (float)columns);

        for (int i = 0; i < count; i++)
        {
            int row = i / columns;
            int column = i % columns;
            float offsetX = (column - (columns - 1) * 0.5f) * FormationSpacing;
            float offsetY = ((rows - 1) * 0.5f - row) * FormationSpacing;
            offsets.Add(new Vector2(offsetX, offsetY));
        }

        return offsets;
    }

    private void RefreshCommandUi(string overrideHint = null)
    {
        Player_UI.Instance?.SetSoldierCommandState(
            selectedSoldiers.Count > 0,
            pendingCommandMode,
            waitingForObserveSecondPoint,
            overrideHint);
    }

    private bool IsPointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private static Rect GetScreenRect(Vector2 screenStart, Vector2 screenEnd)
    {
        Vector2 min = Vector2.Min(screenStart, screenEnd);
        Vector2 max = Vector2.Max(screenStart, screenEnd);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private void OnGUI()
    {
        if (!isDraggingSelection || dragTexture == null)
        {
            return;
        }

        Rect rect = GetScreenRect(
            new Vector2(dragStartScreen.x, Screen.height - dragStartScreen.y),
            new Vector2(Mouse.current.position.ReadValue().x, Screen.height - Mouse.current.position.ReadValue().y));

        DrawScreenRect(rect, dragFillColor);
        DrawScreenRectBorder(rect, 2f, dragBorderColor);
    }

    private void DrawScreenRect(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, dragTexture);
        GUI.color = previous;
    }

    private void DrawScreenRectBorder(Rect rect, float thickness, Color color)
    {
        DrawScreenRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
        DrawScreenRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
        DrawScreenRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
        DrawScreenRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
    }
}
