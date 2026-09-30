# 失踪 拡張Mod

[失踪 ～タケシ。お前の言う通りだった。あの廃村はヤバすぎる。～（Steam）](https://store.steampowered.com/app/2775310/) の操作・表示・視点・照明を調整する非公式Modです。用途別の5つのプラグインで構成されています。

本Modの企画・設計とゲーム内での動作確認は、本Modの制作者が行いました。制作にはOpenAI Codexを使用しています。ゲーム本体のコード・画像・音声・フォントなどは配布物に含んでいません。

## プラグイン一覧

本Modは、ゲームにプラグインを読み込むための仕組みである **BepInEx** を使って動作します。BepInExを先にインストールしてから、使用したいプラグインを追加してください。

**各プラグインは単独でインストールできます。** 必要なDLLだけを選ぶことも、5本をまとめて導入することもできます。

| プラグイン／DLL | 概要 |
| --- | --- |
| **Comfort** — `Disappearance.Comfort.dll` | 3D酔いの軽減を目的に、視点の揺れ・モーションブラーを抑制し、水平視野角（FOV）をオリジナルより広げられるようにします。既存の明るさ設定に加え、新たに追加したFOVとマウス／コントローラー別の感度設定を保存し、次回起動時にも保持できるようにします。周辺減光のON／OFFや、プレイヤー照明のスポットライト／環境照明／OFFも切り替えられます。 |
| **GameplayQoL** — `Disappearance.GameplayQoL.dll` | ゲームを遊びやすくする機能（QoL）として、走行切替と会話の一括スキップを追加します。また、チェックポイントの保存・再開地点選択によるセーブ機能を追加します。オリジナルでは照準を合わせて調整していた南京錠に専用の操作画面を追加し、操作しやすくします。村長宅の鍵を壁越しに入手できる不具合の修正、森の看板発光の切替、草の表示調整も行います。 |
| **Lighting** — `Disappearance.Lighting.dll` | 街灯やトンネル灯に光のにじみ（ハロー）を追加し、発光の見え方を調整します。車両のライトも改善します。また、村の入口の橋付近で、距離によって灯具の高さが変わる不具合を修正します。 撮影カメラのレンズに光の反射を加え、見え方を調整します。|
| **Performance** — `Disappearance.Performance.P6.dll` | パフォーマンスの改善や処理負荷の比較のため、影・ツタ・ポストエフェクト・vSyncを切り替えられるようにします。FPS・フレーム時間などの診断表示も提供します。初期設定ではパフォーマンスの改善のため、点光源の影をOFFにしています。 |
| **UIControls** — `Disappearance.UIControls.dll` | 4Kなどの高解像度で小さく表示される字幕・VN（会話シーン）・メニューのサイズを調整します。操作表や操作案内に追加操作を表示し、十字キーでのメニュー操作を追加します。接続コントローラーに応じたボタン表記の自動判定に対応し、設定からXbox型・Switch型・PS型を手動で選ぶこともできます。照準の見やすさも調整します。 |

### 組み合わせについて

- **Comfort ＋ UIControls**：感度・視野角・明るさを設定画面から調整できます。Comfort単独の場合は設定ファイルとショートカットを使用します。
- **GameplayQoL ＋ UIControls**：追加操作の案内を共通の表示に統合します。GameplayQoLの機能自体はUIControlsなしでも利用できます。
- **Performance ＋ Comfort／GameplayQoL**：右上の診断表示に、照明・視野角・看板発光などの状態も追加表示します。Performance単独では性能と描画比較の情報を表示します。

5本を組み合わせた構成で通しプレイと主要機能を確認しています。全5本それぞれの完全単独試験や、全イベント・全分岐の網羅試験は実施していません。

## 動作環境

- **失踪 ～タケシ。お前の言う通りだった。あの廃村はヤバすぎる。～ Steam版**（Windows x64）。
- **BepInEx 6.0.0** : [BepInEx Bleeding Edge（BE）builds](https://builds.bepinex.dev/projects/bepinex_be)から、名前が `BepInEx-Unity.Mono-win-x64-6.0.0` で始まる最新のZIPを使用してください。`BepInEx-Unity.Mono-win-x64-6.0.0-be.788+5b766a3.zip` で動作確認しています。

## インストール

1. ゲームを終了します。
2. BepInEx未導入の場合は上記ZIPを展開し、その中身を `Disappearance.exe` のあるゲームフォルダーに配置します。
3. 使用したいプラグインに対応するDLLをゲームフォルダーの `BepInEx/plugins` に配置します。フォルダーがなければ作成してください。
4. 付属の `Launch-Disappearance.ps1` を `Disappearance.exe` と同じフォルダーに配置します。
5. Steamのプロパティ → 一般 → 起動オプションに、以下の1行を設定します。

   ```text
   "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File ".\Launch-Disappearance.ps1" %command%
   ```

6. Steamからゲームを起動します。`BepInEx/LogOutput.log` の `Loading [...]` で、選んだプラグインが読み込まれていることを確認できます。

このゲームの日本語パスから直接起動すると、BepInExの `imageOpen` エラーが発生するため、上記の起動スクリプトを使用します。スクリプトは一時的な英字のドライブ名を割り当ててゲームを起動し、ゲーム終了後にその割り当てを解除します。

通常の起動に戻す場合は、Steamの起動オプションを削除します。

## 主な追加操作

以下のコントローラー表記はXbox型です。ゲーム内のグリフは表示設定に従います。

| 操作 | キーボード | コントローラー | 担当 |
| --- | --- | --- | --- |
| 走行切替 | V | Y | GameplayQoL |
| 走行（長押し） | Shift | 左スティック押込み／LB | GameplayQoL |
| 会話・VNの一括スキップ（1秒長押し） | Space | B | GameplayQoL |
| 再開地点選択 | Ctrl＋F2 | LB＋RB＋View／Select | GameplayQoL |
| 全再開地点を解放（選択画面内） | Ctrl＋Shift＋U | LB＋RB＋Y | GameplayQoL |
| 通常プレイの操作案内表示切替 | H | View／Select | UIControls |
| 設定の選択項目を初期値に戻す | R | Y | UIControls ＋ Comfort |
| 視野角を縮小／拡大／初期値に戻す | F3／F4／F5 | — | Comfort |
| 照明モード切替 | Home | — | Comfort |
| 照射角の視野角追従切替 | End | — | Comfort |
| 周辺減光切替 | Delete | — | Comfort |
| 追加ハロー切替 | Ctrl＋Shift＋L | — | Lighting |
| 森の看板発光切替 | G | — | GameplayQoL |
| 右上の診断パネル表示切替 | Ctrl＋Shift＋F1 | — | Performance |
| 太陽影／点光源影／ツタ影／ツタのインスタンシング／ポストエフェクト／vSync | F6／F7／F8／F9／F10／F11 | — | Performance |

Ctrl＋Shift＋Lは追加ハローのみを切り替えます。起動時はONで、操作と現在の状態は右上の画面・照明調整オーバーレイに表示されます。ポーズ配下のメニューはB／Esc／Backspaceで前の画面に戻れます（UIControls導入時）。設定では上下で項目を選択し、左右で値を調整します。全地点解放には確認画面があり、解放状態は元に戻せません。

## 設定・チェックポイント

設定ファイルは初回起動後、`BepInEx/config` に作成されます。手動で編集する場合はゲームを終了してください。

| 担当 | ファイル |
| --- | --- |
| Comfort | `local.disappearance.comfort.cfg` |
| GameplayQoL | `local.disappearance.gameplayqol.cfg` |
| Lighting | `local.disappearance.lighting.cfg` |
| Performance | `local.disappearance.performance.v2.cfg` |
| UIControls | `local.disappearance.uicontrols.cfg` |
| チェックポイント保存 | `local.disappearance.gameplayqol.checkpoints.json` |

新規設定では照射角の視野角追従・周辺減光はON、看板発光はOFFです。更新時は既存の設定を優先します。

チェックポイントは決められた再開地点からの再開です。任意位置やすべての所持品・イベント状態を保存する機能ではありません。再開地点より後の進行状況は保持されません。未到達の再開地点は一覧に表示されません。

## プラグインの追加・更新・削除

- **追加**：ゲームを終了し、まだ導入していないプラグインに対応するDLLを `BepInEx/plugins` に配置します。
- **更新**：ゲームを終了し、更新したいプラグインのDLLを新しいDLLで上書きします。設定・チェックポイントは引き継げます。
- **一部またはすべてを削除**：ゲームを終了し、不要なプラグインに対応するDLLを `BepInEx/plugins` から削除します。設定・チェックポイントは残しておけます。BepInEx本体や他Modのファイルは削除しないでください。

## 既知の不具合・確認範囲

- トンネル付近に黒いアーチ状の帯が残ることがあります。追加ハローをOFFにしても出るゲーム側の既知の表示不具合として保留しています。
- Rai PalからのUUVR導入で `doorstop_config.ini` が別のPreloaderへ書き換わり、本Modが読み込まれなくなった事例があります。UUVRとの同時使用は未検証です。
- 性能改善の程度は環境や設定に依存します。F7・F9などの切替はゲーム内比較で問題がないことを確認していますが、改善率を保証するものではありません。

## 制作・ライセンス

本Modのコード・ドキュメントはOpenAI Codexを用いて制作しました。ゲーム本体およびゲーム素材の権利は、それぞれの権利者に帰属します。BepInExなど外部ソフトウェアのライセンスは各提供元の条件に従います。

本Modの独自コード・ドキュメントは **0BSD（Zero-Clause BSD）** で提供します。商用利用・改変・再配布が可能で、著作権表示やクレジットの継承を条件にしません。無保証・免責の詳細は [LICENSE](LICENSE) を参照してください。ゲーム本体・素材や外部ソフトウェアには、それぞれの権利・ライセンスが適用されます。


## ソースからのビルド

配布DLLは `plugins/`、ソースは `src/` に含まれます。.NET SDKを用意し、BepInEx導入済みのゲームフォルダーを `GameRoot` に指定してビルドできます。以下はComfortをビルドする例です。

```powershell
dotnet build src/Comfort/Comfort.csproj -c Release -p:GameRoot="C:\Games\Disappearance"
```

他のプラグインをビルドする場合は、上記コマンドの `src/Comfort/Comfort.csproj` を、対象に応じて `src/GameplayQoL/GameplayQoL.csproj`、`src/Lighting/Lighting.csproj`、`src/PerformanceV2/PerformanceV2.csproj`、`src/UIControls/UIControls.csproj` に置き換えてください。`GameRoot` の例示パス `C:\Games\Disappearance` も、実際のゲームフォルダーのパスに置き換えてください。

参照するゲーム本体・Unity・BepInExのDLLは同梱していません。
