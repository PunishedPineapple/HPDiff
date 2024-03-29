﻿using System;
using System.Collections.Generic;
using System.IO;

using CheapLoc;

using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

using HPDiff.Services;

namespace HPDiff;

public sealed class Plugin : IDalamudPlugin
{
	public Plugin( DalamudPluginInterface pluginInterface )
	{
		//	API Access
		pluginInterface.Create<Service>();
		mPluginInterface = pluginInterface;

		//	Configuration
		mConfiguration = mPluginInterface.GetPluginConfig() as Configuration;
		if( mConfiguration == null )
		{
			mConfiguration = new Configuration();
			mConfiguration.DiffGaugeConfigs.Add( new( GaugeConfigPresets.DSR_Phase6 ) );
			mConfiguration.DiffGaugeConfigs.Add( new( GaugeConfigPresets.TEA_Phase1 ) );
		}
		mConfiguration.Initialize( mPluginInterface );

		//	Localization and Command Initialization
		OnLanguageChanged( mPluginInterface.UiLanguage );

		//	UI Initialization
		mUI = new PluginUI( this, mConfiguration );
		mPluginInterface.UiBuilder.Draw += DrawUI;
		mPluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
		mUI.Initialize();

		//	Event Subscription
		mPluginInterface.LanguageChanged += OnLanguageChanged;
		Service.Framework.Update += OnGameFrameworkUpdate;
	}

	public void Dispose()
	{
		Service.Framework.Update -= OnGameFrameworkUpdate;
		mPluginInterface.UiBuilder.Draw -= DrawUI;
		mPluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;
		mPluginInterface.LanguageChanged -= OnLanguageChanged;
		Service.CommandManager.RemoveHandler( mTextCommandName );
		mUI.Dispose();
	}

	private void OnLanguageChanged( string langCode )
	{
		var allowedLang = new List<string>{ /*"de", "ja", "fr", "it", "es"*/ };

		Service.PluginLog.Information( "Trying to set up Loc for culture {0}", langCode );

		if( allowedLang.Contains( langCode ) )
		{
			Loc.Setup( File.ReadAllText( Path.Join( Path.Join( mPluginInterface.AssemblyLocation.DirectoryName, "Resources\\Localization\\" ), $"loc_{langCode}.json" ) ) );
		}
		else
		{
			Loc.SetupWithFallbacks();
		}

		//	Set up the command handler with the current language.
		if( Service.CommandManager.Commands.ContainsKey( mTextCommandName ) )
		{
			Service.CommandManager.RemoveHandler( mTextCommandName );
		}
		Service.CommandManager.AddHandler( mTextCommandName, new CommandInfo( ProcessTextCommand )
		{
			HelpMessage = "Opens the configuration window."
		} );
	}

	private void ProcessTextCommand( string command, string args )
	{
		if( command == mTextCommandName ) mUI.mSettingsWindowVisible = true;
	}

	public void OnGameFrameworkUpdate( IFramework framework )
	{
		GaugeDrawData.mShouldDraw = false;
		if( Service.ClientState.TerritoryType == 0 ) return;

		foreach( var config in mConfiguration.DiffGaugeConfigs )
		{
			if( config != null &&
				config.mEnabled &&
				config.mTerritoryType == Service.ClientState.TerritoryType )
			{
				var enemy1 = GetEnemyForName( config.mEnemy1Name ) as BattleChara;
				var enemy2 = GetEnemyForName( config.mEnemy2Name ) as BattleChara;
				if( enemy1 != null && enemy2 != null )
				{
					GaugeDrawData.mShouldDraw = true;
					GaugeDrawData.mEnemy1Name = config.mEnemy1Name;
					GaugeDrawData.mEnemy2Name = config.mEnemy2Name;
					GaugeDrawData.mLeftColor = config.mLeftColor;
					GaugeDrawData.mRightColor = config.mRightColor;
					GaugeDrawData.mGaugeHalfRange_Pct = config.mGaugeHalfRange_Pct;
					GaugeDrawData.mDiffThreshold_Pct = config.mDiffThreshold_Pct;
					GaugeDrawData.mEnemy1HP_Pct = enemy1 != null ? (float)enemy1.CurrentHp / (float)enemy1.MaxHp * 100f : 0f;
					GaugeDrawData.mEnemy2HP_Pct = enemy2 != null ? (float)enemy2.CurrentHp / (float)enemy2.MaxHp * 100f : 0f;
					break;
				}
			}
		}
	}

	private unsafe GameObject GetEnemyForName( string name )
	{
		if( name is null or "" ) return null;

		if( FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance() != null &&
			FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->GetUiModule() != null &&
			FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->GetUiModule()->GetRaptureAtkModule() != null )
		{
			var atkArrayDataHolder = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->GetUiModule()->GetRaptureAtkModule()->AtkModule.AtkArrayDataHolder;
			if( atkArrayDataHolder.NumberArrayCount >= 22 )
			{
				var pEnmityListArray = atkArrayDataHolder.NumberArrays[21];
				int enemyCount = pEnmityListArray->AtkArrayData.Size > 1 ? pEnmityListArray->IntArray[1] : 0;

				for( int i = 0; i < enemyCount; ++i )
				{
					int index = 8 + i * 6;
					if( index >= pEnmityListArray->AtkArrayData.Size ) return null;
					UInt32 OID = (UInt32)pEnmityListArray->IntArray[index];
					var gameObject = Service.ObjectTable.SearchById( OID );
					if( gameObject?.Name.TextValue == name ) return gameObject;
				}
			}
		}

		return null;
	}

	private void DrawUI()
	{
		mUI.Draw();
	}

	private void DrawConfigUI()
	{
		mUI.mSettingsWindowVisible = true;
	}

	public string Name => "HP Difference Gauge";
	private const string mTextCommandName = "/phpdiff";

	private readonly DalamudPluginInterface mPluginInterface;
	private readonly Configuration mConfiguration;
	private readonly PluginUI mUI;

	internal readonly DiffGaugeDrawData GaugeDrawData = new();
}
