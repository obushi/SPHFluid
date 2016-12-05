//https://gist.github.com/TAK-EMI/d67a13b6f73bed32075d

using UnityEngine;
using System.Collections;

// クラス名が被っているといけないので、namespaceで囲む
namespace TAK_CameraController
{
	// マウスのボタンをあらわす番号がわかりにくかったので名前を付けた
	enum MouseButtonDown
	{
		MBD_LEFT = 0,
		MBD_RIGHT,
		MBD_MIDDLE,
	};

	public class CameraPan : MonoBehaviour
	{
		[SerializeField]
		// privateなメンバもインスペクタで編集したいときに付ける
		private GameObject
			focusObj = null;
		// 注視点となるオブジェクト
		
		private Vector3 oldPos;
		// マウスの位置を保存する変数
		
		// 注視点オブジェクトが未設定の場合、新規に生成する
		void setupFocusObject (string name)
		{
			GameObject obj = this.focusObj = new GameObject (name);
			obj.transform.position = Vector3.zero;
			
			return;
		}

		void Start ()
		{
			// 注視点オブジェクトの有無を確認
			if (this.focusObj == null)
				this.setupFocusObject ("CameraFocusObject");
			
			// 注視点オブジェクトをカメラの親にする
			Transform trans = this.transform;
			transform.parent = this.focusObj.transform;
			
			// カメラの方向ベクトル(ローカルのZ方向)を注視点オブジェクトに向ける
			trans.LookAt (this.focusObj.transform.position);
			
			return;
		}

		void Update ()
		{
			// マウス関係のイベントを関数にまとめる
			this.mouseEvent ();
			
			return;
		}
		
		// マウス関係のイベント
		void mouseEvent ()
		{
			// マウスホイールの回転量を取得
			float delta = Input.GetAxis ("Mouse ScrollWheel");
			// 回転量が0でなければホイールイベントを処理
			if (delta != 0.0f)
				this.mouseWheelEvent (delta);
			
			// マウスボタンが押されたタイミングで、マウスの位置を保存する
			if (Input.GetMouseButtonDown ((int)MouseButtonDown.MBD_LEFT) ||
			    Input.GetMouseButtonDown ((int)MouseButtonDown.MBD_MIDDLE) ||
			    Input.GetMouseButtonDown ((int)MouseButtonDown.MBD_RIGHT))
				this.oldPos = Input.mousePosition;
			
			// マウスドラッグイベント
			this.mouseDragEvent (Input.mousePosition);
			
			return;
		}
		
		// マウスホイールイベント
		void mouseWheelEvent (float delta)
		{
			// 注視点からカメラまでのベクトルを計算
			Vector3 focusToPosition = this.transform.position - this.focusObj.transform.position;
			
			// ホイールの回転量を元に上で求めたベクトルの長さを変える
			Vector3 post = focusToPosition * (1.0f + delta);
			
			// 長さを変えたベクトルの長さが一定以上あれば、カメラの位置を変更する
			if (post.magnitude > 0.01f)
				this.transform.position = this.focusObj.transform.position + post;
			
			return;
		}
		
		// マウスドラッグイベント関数
		void mouseDragEvent (Vector3 mousePos)
		{
			// マウスの現在の位置と過去の位置から差分を求める
			Vector3 diff = mousePos - oldPos;
			
			// 差分の長さが極小数より小さかったら、ドラッグしていないと判断する
			if (diff.magnitude < Vector3.kEpsilon)
				return;
			
			if (Input.GetMouseButton ((int)MouseButtonDown.MBD_LEFT)) {
				// マウス左ボタンをドラッグした場合(なにもしない)
			} else if (Input.GetMouseButton ((int)MouseButtonDown.MBD_MIDDLE)) {
				// マウス中ボタンをドラッグした場合(注視点の移動)
				this.cameraTranslate (-diff / 10.0f);
				
			} else if (Input.GetMouseButton ((int)MouseButtonDown.MBD_RIGHT)) {
				// マウス右ボタンをドラッグした場合(カメラの回転)
				this.cameraRotate (new Vector3 (diff.y, diff.x, 0.0f));
			}
			
			// 現在のマウス位置を、次回のために保存する
			this.oldPos = mousePos;
			
			return;
		}
		
		// カメラを移動する関数
		void cameraTranslate (Vector3 vec)
		{
			Transform focusTrans = this.focusObj.transform;
			Transform trans = this.transform;
			
			// カメラのローカル座標軸を元に注視点オブジェクトを移動する
			focusTrans.Translate ((trans.right * -vec.x) + (trans.up * vec.y));
			
			return;
		}
		
		// カメラを回転する関数
		public void cameraRotate (Vector3 eulerAngle)
		{
			Vector3 focusPos = this.focusObj.transform.position;
			Transform trans = this.transform;
			
			// 回転前のカメラの情報を保存する
			Vector3 preUpV, preAngle, prePos;
			preUpV = trans.up;
			preAngle = trans.localEulerAngles;
			prePos = trans.position;
			
			// カメラの回転
			// 横方向の回転はグローバル座標系のY軸で回転する
			trans.RotateAround (focusPos, Vector3.up, eulerAngle.y);
			// 縦方向の回転はカメラのローカル座標系のX軸で回転する
			trans.RotateAround (focusPos, trans.right, -eulerAngle.x);
			
			// カメラを注視点に向ける
			trans.LookAt (focusPos);
			
			// ジンバルロック対策
			// カメラが真上や真下を向くとジンバルロックがおきる
			// ジンバルロックがおきるとカメラがぐるぐる回ってしまうので、一度に90度以上回るような計算結果になった場合は回転しないようにする(計算を元に戻す)
			Vector3 up = trans.up;
			if (Vector3.Angle (preUpV, up) > 90.0f) {
				trans.localEulerAngles = preAngle;
				trans.position = prePos;
			}
			
			return;
		}
	}
}