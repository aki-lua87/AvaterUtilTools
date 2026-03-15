# AAU (AvatarUtils)

VRChat アバター向けの**非破壊**ユーティリティコンポーネント集です。  
VRC SDK のビルドパイプライン (`IVRCSDKPreprocessAvatarCallback`) に統合されており、アップロード直前に自動実行されます。元のアセットは一切変更されません。

---

## 動作要件

- Unity 2022.x (VRChat SDK 対応バージョン)
- VRCSDK3 Avatars
- liltoon (ColorMaskTextureGenerator を使用する場合)

---

## インストール

`Assets/aki_lua87/AAU/` フォルダをプロジェクトにそのまま配置してください。

---

## コンポーネント一覧

### 1. DefaultExpressionOverride

**アバターのデフォルト表情を非破壊で上書きするコンポーネント。**

FX AnimatorController 内の指定したアニメーションクリップを、**現在のシェイプキーの状態**でビルド時に差し替えます。元のコントローラーやクリップは変更されません。

#### アタッチ場所

`VRCAvatarDescriptor` と同じ GameObject（アバタールート）

#### インスペクタープロパティ

| プロパティ | 説明 |
|---|---|
| **FX Controller** | FX レイヤーの AnimatorController |
| **Face Mesh** | シェイプキーを読み取る SkinnedMeshRenderer（通常 `Body`）|
| **Target Clips** | 上書き対象のアニメーションクリップ |

#### インスペクターのボタン

- **FX を自動検出してセット** — `VRCAvatarDescriptor` の FX レイヤーからコントローラーを自動取得します
- **Body を自動検出してセット** — 子オブジェクトから `Body` という名前の SkinnedMeshRenderer を検索します
- **FXから自動検出** — FX コントローラー内の全クリップを一覧表示し、上書き対象を選択できます
- **現在の値を確認** (折りたたみ) — 非ゼロのシェイプキーの現在値を一覧表示します

#### 動作フロー

1. `FaceMesh` からすべてのシェイプキー名と現在の値を読み取る
2. `TargetClips` の各クリップをコピーし、すべてのシェイプキーを定数カーブで上書き
3. `FXController` をコピーし、コピー内のクリップを差し替えたものと置換
4. `VRCAvatarDescriptor` の FX レイヤーを生成したコントローラーに切り替え

#### 生成物

```
Assets/aki_lua87_AAU_Generated/DefaultExpressionOverride/
  ├── <ClipName>_ExpressionOverride.anim  (クリップのコピー)
  └── <ControllerName>_ExpressionOverride.controller  (コントローラーのコピー)
```

---

### 2. ColorMaskTextureGenerator

**指定した色に基づいてカラーマスクテクスチャを生成し、マテリアルのテクスチャプロパティに非破壊で適用するコンポーネント。**  
liltoon の各種テクスチャスロットに対応しています。

#### アタッチ場所

アバター階層内の任意の GameObject

#### インスペクタープロパティ

| プロパティ | 説明 |
|---|---|
| **ソーステクスチャ** | マスク生成の元になるテクスチャ |
| **ターゲットカラー** | マスク対象にする色 |
| **色誤差 (Tolerance)** | 0〜1 の範囲で色の一致許容量を指定 |
| **一致部分を白にする** | ON: 一致→白・非一致→黒 / OFF: 反転 |
| **グラデーション** | ON: 色の距離に応じてなめらかなグラデーションマスクを生成 |
| **対象マテリアル** | マスクを適用するマテリアル |
| **テクスチャプロパティ** | マテリアル上の適用先テクスチャプロパティ（ドロップダウンで選択）|
| **機能を強制 ON** | liltoon のテクスチャ機能トグルを自動で有効化する |

#### インスペクターのUI機能

- **スポイト** — テクスチャプレビュー上をクリックして色を取得します
- **マスクプレビュー** — インスペクター内にマスクのリアルタイムプレビューを表示します
- **※画像として書き出す** — マスクテクスチャを PNG ファイルとして任意の場所に保存します
- **▶ Scene プレビュー開始** — 実際のシーン上でマスクを適用した状態をプレビューします（元のマテリアルアセットは変更されません）

#### liltoon 対応プロパティ

`機能を強制 ON` オプションに対応しているプロパティ：

| テクスチャプロパティ | 有効化されるトグル |
|---|---|
| `_EmissionMap` | `_UseEmission` |
| `_Emission2ndMap` | `_UseEmission2nd` |
| `_NormalMap` | `_UseBumpMap` |
| `_NormalMap2nd` | `_UseBump2ndMap` |
| `_MatCapTex` | `_UseMatCap` |
| `_MatCap2ndTex` | `_UseMatCap2nd` |
| `_RimColorTex` | `_UseRim` |
| `_MainTex2nd` | `_UseMain2ndTex` |
| `_MainTex3rd` | `_UseMain3rdTex` |
| `_ShadowColorTex` | `_UseShadow` + `_ShadowColorType = 1` |

#### 動作フロー

1. `SourceTexture` の各ピクセルと `TargetColor` の色距離を計算
2. `Tolerance` と `UseGradient` の設定に基づいてグレースケールマスクを生成
3. `TargetMaterial` をクローンしてマスクテクスチャをセット
4. アバター以下の全 Renderer で `TargetMaterial` を参照しているスロットをクローンに差し替え

---

### 3. DestroyOnUpload

**アップロード時に GameObject を削除するコンポーネント。**

コンポーネントがアタッチされた GameObject を、ビルド前処理のタイミングで `DestroyImmediate` します。エディター上のみ存在させたいデバッグ用オブジェクトや、アップロード後に不要になるオブジェクトの削除に使用します。

#### アタッチ場所

削除したい GameObject

---

## ベースシステム

### AvatarModify (抽象クラス)

すべてのコンポーネントが継承する基底クラス。  
`Apply(GameObject avatarRoot)` メソッドを実装することで、ビルド時に自動実行される処理を定義できます。

### AvatarModifyProcessor

`IVRCSDKPreprocessAvatarCallback` を実装したクラス。  
アバタービルド直前にアバター以下のすべての `AvatarModify` コンポーネントを収集し、`Apply` を呼び出します。

---

## 注意事項

- 生成されたアセットは `Assets/aki_lua87_AAU_Generated/` に保存されます。再ビルド時には自動で上書きされます
- `ColorMaskTextureGenerator` はビルド時にマテリアルをクローンして適用するため、元のマテリアルアセットは変更されません
- `DefaultExpressionOverride` の `TargetClips` には FX コントローラー内で実際に使用されているクリップを指定してください。使用されていないクリップを指定した場合は警告が表示されます
- Scene プレビュー機能は `AnimationMode` を使用しています。プレビュー中は他の AnimationMode と併用できません
