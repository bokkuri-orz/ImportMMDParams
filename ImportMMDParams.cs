//-------------------------------------------------------------------------------
// ImportMMDParams.cs
// 
// UnityにインポートしたMMDモデルに、後からコリジョン、ジョイント、リジッドボディを追加インポートするツール。
// 
// Author : bokkuri
// 
// ※MMDを完全に再現していなかったり、体感で合わせてあるパラメータがあったりします。
//   「大体合っている」という事を前提にご利用願います。
// 
//-------------------------------------------------------------------------------

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

using UnityEditor;

/// <summary>
/// MMDパラメータ(ジョイント、剛体)をインポートする
/// </summary>
public class ImportMMDParams : EditorWindow
{
	private bool m_isInit = false;

	private GameObject m_rootObject;

	private float m_scale = 0.2f;	// コリジョン・ボーンのサイズ、ポジションに対するスケール。デフォルト値は謎の0.2倍。

	private TextAsset m_boneData;
	private TextAsset m_rigidData;
	private TextAsset m_jointData;

	private List<BoneData> m_boneList = new List<BoneData>();
	private List<RigidData> m_rigidList = new List<RigidData>();
	private List<JointData> m_jointList = new List<JointData>();

	private Vector2 m_scrBone = Vector2.zero;
	private Vector2 m_scrRigid = Vector2.zero;
	private Vector2 m_scrJoint = Vector2.zero;

	private LayerMask m_clothLayer;


	/// <summary>
	/// ボーンデータ
	/// </summary>
	/// <remarks>
	/// 必要な物だけ拾う
	/// </remarks>
	private class BoneData
	{
		public string m_nameJP;
		public string m_nameEN;
		public int m_index;
	}

	/// <summary>
	/// 剛体データ
	/// </summary>
	/// <remarks>
	/// 必要な物だけ拾う
	/// </remarks>
	private class RigidData
	{
		public enum CollType
		{
			Sphere = 0,
			Box = 1,
			Capsule = 2
		}

		public int m_rigidType;		//4:剛体タイプ(0:Bone/1:物理演算/2:物理演算+ボーン追従)

		public CollType m_collType = CollType.Sphere;	//7:コリジョン形状
		public Vector3 m_size;		//8,9,10:サイズ
		public Vector3 m_pos;		//11,12,13:絶対座標
		public Vector3 m_rotDeg;	//14,15,16:絶対角度

		public float m_mass;		//17:質量
		public float m_drag;		//18:移動減衰
		public float m_angularDrag;	//19:回転減衰

		public string m_boneName;
		public int m_boneIdx;

		/// <summary>
		/// 対象のオブジェクト(ボーン)にコリジョンを付ける
		/// </summary>
		/// <param name="rootPos">モデルrootの座標</param>
		/// <param name="target">対象のボーン</param>
		/// <param name="scale">スケーリング</param>
		public void AddCollision(Vector3 rootPos, GameObject target, float scale, LayerMask physicsLayer)
		{
			// コリジョン用GameObject
			// 剛体はボーンと別に座標を持っていて、Collider.Centerだけでは実現できない
			GameObject go = new GameObject();

			// 角度変換用
			GameObject tempRootObj = new GameObject();

			go.transform.position = new Vector3(m_pos.x, m_pos.y, m_pos.z) * scale;
			go.transform.rotation = Quaternion.Euler(m_rotDeg.x, m_rotDeg.y, m_rotDeg.z);

			tempRootObj.transform.position = Vector3.zero;
			tempRootObj.transform.rotation = Quaternion.identity;
			go.transform.parent = tempRootObj.transform;
			tempRootObj.transform.position = rootPos;
			tempRootObj.transform.rotation = Quaternion.Euler(0, 180, 0);


			switch (m_collType)
			{
			case CollType.Sphere:
				{
					SphereCollider coll = go.AddComponent<SphereCollider>();
					coll.radius = m_size.x * scale;
				}
				break;
			case CollType.Box:
				{
					// MMDの剛体は、半径1の球が、各辺1の立方体にちょうど収まるサイズ。
					// なので、サイズを2倍にすると他の形状とサイズが合う。
					BoxCollider coll = go.AddComponent<BoxCollider>();
					Vector3 size = Vector3.zero;
					size.x = m_size.x * scale * 2;
					size.y = m_size.y * scale * 2;
					size.z = m_size.z * scale * 2;
					coll.size = size;
				}
				break;
			case CollType.Capsule:
				{
					// MMDのカプセルは、高さ(height)に半径部分も含まれる。
					// Unityのカプセルは半径部分を除いた円柱のみの高さが height。
					CapsuleCollider coll = go.AddComponent<CapsuleCollider>();
					coll.radius = m_size.x * scale;
					coll.height = (m_size.y + m_size.x * 2) * scale;
					coll.direction = 1;
				}
				break;
			}

			if (m_rigidType == 0)
			{
				go.name = "coll_" + target.name;
				go.transform.parent = target.transform;
			}
			else
			{
				go.name = "phys_" + target.name;
				go.transform.parent = target.transform;
				go.gameObject.layer = physicsLayer.value;
			}

			Rigidbody rb = target.rigidbody;

			if (rb == null)
			{
				if (m_rigidType == 0)
				{
					// RigidBodyを付けるけど、固定する。
					rb = target.AddComponent<Rigidbody>();
					rb.useGravity = false;
					rb.isKinematic = true;
				}
				else
				{
					// とりあえず、m_rigidType != 0 は物理演算が必要。
					rb = target.AddComponent<Rigidbody>();
					rb.useGravity = true;
					rb.isKinematic = false;

					// 物理演算パラメータ
					rb.mass = m_mass;
					rb.drag = m_drag;
					rb.angularDrag = m_angularDrag;

					rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

					//レイヤーはColliderに対して設定するのが正しい
					//rb.gameObject.layer = physicsLayer.value;
				}
			}

			DestroyImmediate(tempRootObj);
			tempRootObj = null;
		}
	}

	/// <summary>
	/// ジョイントデータ
	/// </summary>
	private class JointData
	{
		public string m_name;
		public string m_boneName;
		public string m_parentBoneName;

		public Vector3 m_linerLimitMin;		//12-14:移動下限x,y,z
		public Vector3 m_linerLimitMax;		//15-17:移動上限x,y,z

		public Vector3 m_angularLimitMin;	//18-20:回転下限x,y,z
		public Vector3 m_angularLimitMax;	//21-23:回転上限x,y,z

		public Vector3 m_springMove;		//24-26:バネ定数 移動x,y,z
		public Vector3 m_springDeg;			//27-29:バネ定数 回転x,y,z


		/// <summary>
		/// ジョイントを生成する
		/// </summary>
		/// <param name="bone"></param>
		/// <param name="parentBone"></param>
		public void AddJoint(GameObject bone, GameObject parentBone)
		{
			//Debug.Log(bone.name + " -> " + parentBone.name);

			ConfigurableJoint joint = bone.AddComponent<ConfigurableJoint>();
			Rigidbody rd = parentBone.rigidbody;
			SoftJointLimit sjLimit = new SoftJointLimit();

			if (joint != null && rd != null)
			{
				joint.connectedBody = rd;
				joint.breakForce = float.PositiveInfinity;
				joint.breakTorque = float.PositiveInfinity;

				joint.xMotion = Mathf.Abs(m_linerLimitMin.x - m_linerLimitMax.x) <= float.Epsilon ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
				joint.yMotion = Mathf.Abs(m_linerLimitMin.y - m_linerLimitMax.y) <= float.Epsilon ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
				joint.zMotion = Mathf.Abs(m_linerLimitMin.z - m_linerLimitMax.z) <= float.Epsilon ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
				joint.angularXMotion = Mathf.Abs(m_angularLimitMin.x - m_angularLimitMax.x) <= float.Epsilon ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
				joint.angularYMotion = Mathf.Abs(m_angularLimitMin.y - m_angularLimitMax.y) <= float.Epsilon ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
				joint.angularZMotion = Mathf.Abs(m_angularLimitMin.z - m_angularLimitMax.z) <= float.Epsilon ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;

				//lowAngularXLimit
				sjLimit.limit = m_angularLimitMin.x;
				joint.lowAngularXLimit = sjLimit;

				//highAngularXLimit
				sjLimit.limit = m_angularLimitMax.x;
				joint.highAngularXLimit = sjLimit;

				//angularYLimit
				sjLimit.limit = m_angularLimitMax.y;
				joint.angularYLimit = sjLimit;

				//angularZLimit
				sjLimit.limit = m_angularLimitMax.z;
				joint.angularZLimit = sjLimit;
			}
		}
	}


	/// <summary>
	/// ImportMMDParamsのウィンドウを開く
	/// </summary>
	[MenuItem("Custom/MMD Tools/Import MMD Params")]
	public static void Open()
	{
		ImportMMDParams window = EditorWindow.GetWindow<ImportMMDParams>();
		if (window != null)
		{
			window.Init();
			window.Show();
		}
	}

	/// <summary>
	/// 初期化
	/// </summary>
	private void Init()
	{
		if (m_isInit) return;

		m_isInit = true;
	}

	/// <summary>
	/// コリジョン、ジョイント、リジッドボディを作成
	/// </summary>
	/// <param name="rootObject"></param>
	/// <param name="rigidList"></param>
	/// <param name="jointList"></param>
	private void AddCollision(GameObject rootObject, List<RigidData> rigidList, List<JointData> jointList)
	{
		// 剛体情報から、コリジョンとリジッドボディを生成する
		foreach (RigidData rd in rigidList)
		{
			string targetName;

			targetName = rd.m_boneName;

			Transform tf = FindObject(rootObject.transform, targetName);
			if (tf != null)
			{
				// コリジョンを付ける
				rd.AddCollision(m_rootObject.transform.position, tf.gameObject, m_scale, m_clothLayer);
			}
		}

		// ジョイント
		foreach (JointData jd in jointList)
		{
			Transform tf1 = FindObject(rootObject.transform, jd.m_boneName);
			Transform tf2 = FindObject(rootObject.transform, jd.m_parentBoneName);

			if (tf1 != null && tf2 != null)
			{
				// ジョイント生成
				jd.AddJoint(tf1.gameObject, tf2.gameObject);
			}
		}
	}

	/// <summary>
	/// 人体モデルの階層から、名前でTransform検索
	/// </summary>
	/// <param name="target"></param>
	/// <param name="name"></param>
	/// <returns></returns>
	private Transform FindObject(Transform target, string name)
	{
		Transform ret = null;

		int count = target.childCount;
		for (int i = 0; i < count; ++i)
		{
			Transform c = target.GetChild(i);

			if (c.childCount > 0)
			{
				ret = FindObject(c, name);
				if (ret != null) break;
			}

			if (string.Compare(c.name, name) == 0)
			{
				ret = c;
				break;
			}
		}

		return ret;
	}

	/// <summary>
	/// MMD情報CSVの読み込み
	/// </summary>
	/// <param name="rigid"></param>
	/// <param name="bone"></param>
	/// <param name="joint"></param>
	private void LoadCSV(TextAsset rigid, TextAsset bone, TextAsset joint)
	{
		string[] lines = bone.text.Split('\n');

		int count = lines.Length;

		// ボーン情報
		m_boneList.Clear();
		int idx = 0;
		for (int i = 0; i < count; ++i)
		{
			string temp = lines[i].Trim();
			if (temp.Length <= 0) continue;

			if (temp.IndexOf(';') == 0) continue;

			string[] items = temp.Split(',');

			if (items.Length <= 0) continue;

			BoneData bd = new BoneData();
			bd.m_index = idx++;
			bd.m_nameJP = items[1].Replace("\"", "");
			bd.m_nameEN = items[2].Replace("\"", "");

			m_boneList.Add(bd);
		}

		// 剛体情報
		lines = rigid.text.Split('\n');
		count = lines.Length;
		m_rigidList.Clear();
		idx = 0;
		for (int i = 0; i < count; ++i)
		{
			string temp = lines[i].Trim();
			if (temp.Length <= 0) continue;

			if (temp.IndexOf(';') == 0) continue;

			string[] items = temp.Split(',');

			RigidData rd = new RigidData();

			rd.m_boneName = items[3].Replace("\"", "");

			foreach (BoneData bd in m_boneList)
			{
				if (string.Compare(bd.m_nameJP, rd.m_boneName) == 0)
				{
					rd.m_boneIdx = bd.m_index;
				}
			}

			rd.m_rigidType = int.Parse(items[4]);

			rd.m_collType = (RigidData.CollType)int.Parse(items[7]);

			rd.m_size.x = float.Parse(items[8]);
			rd.m_size.y = float.Parse(items[9]);
			rd.m_size.z = float.Parse(items[10]);
			rd.m_pos.x = float.Parse(items[11]);
			rd.m_pos.y = float.Parse(items[12]);
			rd.m_pos.z = float.Parse(items[13]);
			rd.m_rotDeg.x = float.Parse(items[14]);
			rd.m_rotDeg.y = float.Parse(items[15]);
			rd.m_rotDeg.z = float.Parse(items[16]);

			rd.m_mass = float.Parse(items[17]);
			rd.m_drag = float.Parse(items[18]);
			rd.m_angularDrag = float.Parse(items[19]);

			m_rigidList.Add(rd);

			idx++;
		}

		// ジョイント情報
		lines = joint.text.Split('\n');
		count = lines.Length;
		m_jointList.Clear();
		idx = 0;
		for (int i = 0; i < count; ++i)
		{
			string temp = lines[i].Trim();
			if (temp.Length <= 0) continue;

			if (temp.IndexOf(';') == 0) continue;

			string[] items = temp.Split(',');

			JointData jd = new JointData();

			jd.m_name = items[1].Replace("\"", "");
			jd.m_boneName = items[4].Replace("\"", "");
			jd.m_parentBoneName = items[3].Replace("\"", "");

			//12-14:移動下限x,y,z
			jd.m_linerLimitMin.x = float.Parse(items[12]);
			jd.m_linerLimitMin.y = float.Parse(items[13]);
			jd.m_linerLimitMin.z = float.Parse(items[14]);
			//15-17:移動上限x,y,z
			jd.m_linerLimitMax.x = float.Parse(items[15]);
			jd.m_linerLimitMax.y = float.Parse(items[16]);
			jd.m_linerLimitMax.z = float.Parse(items[17]);

			//18-20:回転下限x,y,z
			jd.m_angularLimitMin.x = float.Parse(items[18]);
			jd.m_angularLimitMin.y = float.Parse(items[19]);
			jd.m_angularLimitMin.z = float.Parse(items[20]);
			//21-23:回転上限x,y,z
			jd.m_angularLimitMax.x = float.Parse(items[21]);
			jd.m_angularLimitMax.y = float.Parse(items[22]);
			jd.m_angularLimitMax.z = float.Parse(items[23]);

			//24-26:バネ定数 移動x,y,z
			jd.m_springMove.x = float.Parse(items[24]);
			jd.m_springMove.y = float.Parse(items[25]);
			jd.m_springMove.z = float.Parse(items[26]);
			//27-29:バネ定数 回転x,y,z
			jd.m_springDeg.x = float.Parse(items[27]);
			jd.m_springDeg.y = float.Parse(items[28]);
			jd.m_springDeg.z = float.Parse(items[29]);

			m_jointList.Add(jd);

			idx++;
		}
	}

	/// <summary>
	/// EditorWindowのUI
	/// </summary>
	private void OnGUI()
	{
		LayoutSourceData();

		EditorGUILayout.Space();

		LayoutMMDParams();
	}

	/// <summary>
	/// インポート処理関連のUI
	/// </summary>
	private void LayoutSourceData()
	{
		// MMDパラメータ(CSV)
		m_boneData = EditorGUILayout.ObjectField("Bone CSV", m_boneData, typeof(TextAsset), false) as TextAsset;
		m_rigidData = EditorGUILayout.ObjectField("Rigid CSV", m_rigidData, typeof(TextAsset), false) as TextAsset;
		m_jointData = EditorGUILayout.ObjectField("Joint CSV", m_jointData, typeof(TextAsset), false) as TextAsset;

		// パラメータ読み込みボタン
		EditorGUI.BeginDisabledGroup(m_rigidData == null || m_boneData == null || m_jointData == null);
		if (GUILayout.Button("Load CSV"))
		{
			LoadCSV(m_rigidData, m_boneData, m_jointData);
		}
		EditorGUI.EndDisabledGroup();


		EditorGUILayout.Space();

		// MMDモデルのルートオブジェクト
		m_rootObject = EditorGUILayout.ObjectField("Root GameObject", m_rootObject, typeof(GameObject), true) as GameObject;

		// 全体のスケール値
		m_scale = EditorGUILayout.FloatField("Collision Scale", m_scale);

		// 髪、服等の物理演算用レイヤー
		m_clothLayer = EditorGUILayout.LayerField("Cloth Layer", m_clothLayer); 

		// コリジョン、ジョイント、リジッドボディを付ける
		EditorGUI.BeginDisabledGroup(m_rootObject == null);
		if (GUILayout.Button("Add Collision, Joint, Rigidbody"))
		{
			AddCollision(m_rootObject, m_rigidList, m_jointList);
		}
		EditorGUI.EndDisabledGroup();
	}

	/// <summary>
	/// MMDパラメータ表示
	/// </summary>
	private void LayoutMMDParams()
	{
		GUILayoutOption labelWidthS = GUILayout.Width(32);
		GUILayoutOption labelWidthM = GUILayout.Width(64);
		GUILayoutOption labelWidthL = GUILayout.Width(128);


		EditorGUILayout.BeginHorizontal();


		m_scrBone = EditorGUILayout.BeginScrollView(m_scrBone);

		EditorGUILayout.LabelField("Bone : " + m_boneList.Count);

		foreach (BoneData bd in m_boneList)
		{
			EditorGUILayout.BeginHorizontal();

			EditorGUILayout.LabelField(bd.m_index.ToString(), labelWidthS);
			EditorGUILayout.LabelField(bd.m_nameJP, labelWidthL);
			EditorGUILayout.LabelField(bd.m_nameEN, labelWidthM);
			EditorGUILayout.EndHorizontal();
		}

		EditorGUILayout.EndScrollView();


		m_scrRigid = EditorGUILayout.BeginScrollView(m_scrRigid);

		EditorGUILayout.LabelField("Rigid : " + m_rigidList.Count);

		foreach (RigidData rd in m_rigidList)
		{
			EditorGUILayout.BeginHorizontal();
			
			EditorGUILayout.LabelField(rd.m_boneIdx.ToString(), labelWidthS);
			EditorGUILayout.LabelField(rd.m_boneName, labelWidthL);
			EditorGUILayout.LabelField(rd.m_collType.ToString(), labelWidthM);
			EditorGUILayout.LabelField(rd.m_pos.ToString(), labelWidthL);
			EditorGUILayout.LabelField(rd.m_rotDeg.ToString(), labelWidthL);

			EditorGUILayout.EndHorizontal();
		}

		EditorGUILayout.EndScrollView();


		m_scrJoint = EditorGUILayout.BeginScrollView(m_scrJoint);

		EditorGUILayout.LabelField("Joint : " + m_jointList.Count);

		foreach (JointData jd in m_jointList)
		{
			EditorGUILayout.BeginHorizontal();

			EditorGUILayout.LabelField(jd.m_name, labelWidthM);
			EditorGUILayout.LabelField(jd.m_boneName, labelWidthL);
			EditorGUILayout.LabelField(jd.m_parentBoneName, labelWidthL);

			EditorGUILayout.EndHorizontal();
		}

		EditorGUILayout.EndScrollView();


		EditorGUILayout.EndHorizontal();
	}
}
