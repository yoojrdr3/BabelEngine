using UnityEngine;

namespace Babel
{
    // 탑다운 쿼터뷰 카메라(직교). 어디를 얼마나 확대해 볼지는 PlayerController 가 정한다.
    [RequireComponent(typeof(Camera))]
    public class BattleCamera : MonoBehaviour
    {
        public BattleConfig config;
        public float distance = 80f;

        Camera cam;
        public Camera Cam => cam != null ? cam : (cam = GetComponent<Camera>());

        public void Apply(Vector2 focus, float zoom)
        {
            float s = config.unitScale;
            transform.rotation = Quaternion.Euler(config.camPitch, 0f, 0f);
            transform.position = new Vector3(focus.x * s, 0f, focus.y * s) - transform.forward * distance;
            Cam.orthographic = true;
            Cam.orthographicSize = config.U(config.refHalfHeight) / zoom;
        }

        // 화면 픽셀 → 바닥(y=0) 위의 웹 단위 좌표
        public Vector2 ScreenToSim(Vector2 screen)
        {
            Ray r = Cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            if (Mathf.Abs(r.direction.y) < 1e-5f) return Vector2.zero;
            Vector3 g = r.origin + r.direction * (-r.origin.y / r.direction.y);
            return new Vector2(g.x, g.z) / config.unitScale;
        }

        // 웹 단위 좌표(+높이 m) → IMGUI 좌표
        public Vector2 SimToGui(Vector2 p, float heightMeters)
        {
            float s = config.unitScale;
            Vector3 sp = Cam.WorldToScreenPoint(new Vector3(p.x * s, heightMeters, p.y * s));
            return new Vector2(sp.x, Screen.height - sp.y);
        }
    }
}
