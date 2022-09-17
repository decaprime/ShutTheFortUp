using BepInEx;
using BepInEx.IL2CPP;
using ProjectM.CastleBuilding;
using ProjectM.Scripting;
using ProjectM;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using VampireCommandFramework;
using Wetstone.API;
using UnityEngine;
using System.Collections.Generic;

namespace ShutTheFortUp;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("gg.deca.VampireCommandFramework")]
[BepInDependency("xyz.molenzwiebel.wetstone")]
public class Plugin : BasePlugin
{
	public override void Load()
	{
		Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} version {MyPluginInfo.PLUGIN_VERSION} is loaded!");

		// Register all commands in the assembly with VCF
		CommandRegistry.RegisterAll();
	}

	public enum DoorWindow { Doors, Windows }

	[Command("close-all")]
	public static void CloseAll(ChatCommandContext ctx, DoorWindow? target = null) => ChangeDoorWindows(ctx, false, target);

	[Command("open-all")]
	public static void OpenAll(ChatCommandContext ctx, DoorWindow? target = null) => ChangeDoorWindows(ctx, true, target);

	const float MAX_CASTLE_DISTANCE = 60f;
	private static EntityManager _em => VWorld.Server.EntityManager;
	private static readonly HashSet<int> WINDOW_PREFABS = new() { -1771014048 };

	private static void ChangeDoorWindows(ChatCommandContext ctx, bool open, DoorWindow? target)
	{
		var character = ctx.Event.SenderCharacterEntity;

		// First find the base
		var castleHearts = _em.CreateEntityQuery(ComponentType.ReadOnly<CastleHeart>(), ComponentType.ReadOnly<LocalToWorld>())
							  .ToEntityArray(Allocator.Temp);

		var gameManager = VWorld.Server.GetExistingSystem<ServerScriptMapper>()?._ServerGameManager;
		var playerPos = _em.GetComponentData<LocalToWorld>(character).Position;

		Entity closestCastle = Entity.Null;
		float closest = float.MaxValue;

		foreach (var castle in castleHearts)
		{
			var isPlayerOwned = gameManager._TeamChecker.IsAllies(character, castle);
			if (!isPlayerOwned) continue;

			var castlePos = _em.GetComponentData<LocalToWorld>(castle).Position;
			var distance = Vector3.Distance(castlePos, playerPos);
			if (distance < closest && distance < MAX_CASTLE_DISTANCE)
			{
				closest = distance;
				closestCastle = castle;
			}
		}

		if (closestCastle == Entity.Null) throw ctx.Error("Could not find nearby castle you own");

		// Then find the doors that belong to it
		var doors = _em.CreateEntityQuery(new EntityQueryDesc()
		{
			All = new[] { ComponentType.ReadWrite<Door>(), ComponentType.ReadOnly<CastleHeartConnection>(), ComponentType.ReadOnly<PrefabGUID>(), ComponentType.ReadOnly<Team>() },
			Options = EntityQueryOptions.IncludeDisabled
		}).ToEntityArray(Allocator.Temp);

		int count = 0;
		foreach (var doorEnt in doors)
		{
			var connection = _em.GetComponentData<CastleHeartConnection>(doorEnt);
			if (connection.CastleHeartEntity._Entity != closestCastle) continue;

			if (target != null)
			{
				var prefab = _em.GetComponentData<PrefabGUID>(doorEnt);

				if (target == DoorWindow.Doors && WINDOW_PREFABS.Contains(prefab.GuidHash)) continue;
				if (target == DoorWindow.Windows && !WINDOW_PREFABS.Contains(prefab.GuidHash)) continue;
			}

			var door = _em.GetComponentData<Door>(doorEnt);
			door.OpenState = open;
			_em.SetComponentData<Door>(doorEnt, door);
			count++;
		}

		ctx.Reply($"Changed {count} {(target?.ToString() ?? "Windows and Doors")}");
	}
}