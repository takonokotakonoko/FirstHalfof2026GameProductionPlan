using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 2つのボタンで、画像Aと画像Bの色を切り替えるスクリプト。
///
/// ・ボタンAをクリック → 画像Aを「選択時の色」、画像Bを「非選択時の色」にする
/// ・ボタンBをクリック → 画像Bを「選択時の色」、画像Aを「非選択時の色」にする
///
/// どちらのボタンも、押すとInspectorで指定した武器を
/// PlayerAttack.SaveSelectedWeapon() で保存し、次のシーンのPlayerAttackに反映されます。
///
/// 最初は画像Aが選択された状態（ボタンAを押したときと同じ色・武器）になります。
/// 色はすべてInspectorから設定できます。
///
/// ボタンのOnClickに直接登録したい場合は、
/// SelectA() / SelectB() を指定してください。
/// </summary>
public class ImageColorSwitcher : MonoBehaviour
{
    // =========================================================
    // Inspector設定
    // =========================================================

    [Header("ボタン設定")]

    [Tooltip("クリックすると画像Aを選択状態にするボタン")]
    [SerializeField]
    private Button buttonA;

    [Tooltip("クリックすると画像Bを選択状態にするボタン")]
    [SerializeField]
    private Button buttonB;


    [Header("画像A設定")]

    [Tooltip("色を変える画像A")]
    [SerializeField]
    private Image imageA;

    [Tooltip("ボタンAを押したときの画像Aの色")]
    [SerializeField]
    private Color imageASelectedColor = Color.white;

    [Tooltip("ボタンBを押したときの画像Aの色")]
    [SerializeField]
    private Color imageAUnselectedColor = Color.gray;


    [Header("画像B設定")]

    [Tooltip("色を変える画像B")]
    [SerializeField]
    private Image imageB;

    [Tooltip("ボタンBを押したときの画像Bの色")]
    [SerializeField]
    private Color imageBSelectedColor = Color.white;

    [Tooltip("ボタンAを押したときの画像Bの色")]
    [SerializeField]
    private Color imageBUnselectedColor = Color.gray;


    [Header("武器設定")]

    [Tooltip("ボタンAを押したときに使う武器")]
    [SerializeField]
    private PlayerAttack.WeaponType weaponA = PlayerAttack.WeaponType.Fist;

    [Tooltip("ボタンBを押したときに使う武器")]
    [SerializeField]
    private PlayerAttack.WeaponType weaponB = PlayerAttack.WeaponType.Gun;


    // =========================================================
    // 初期化
    // =========================================================

    private void Awake()
    {
        // ボタンが設定されていれば、クリック時の処理を登録する
        if (buttonA != null)
        {
            buttonA.onClick.AddListener(SelectA);
        }

        if (buttonB != null)
        {
            buttonB.onClick.AddListener(SelectB);
        }
    }


    private void Start()
    {
        // 最初は画像Aを選択状態にしておく
        SelectA();
    }


    private void OnDestroy()
    {
        // 登録したクリック処理を解除する
        if (buttonA != null)
        {
            buttonA.onClick.RemoveListener(SelectA);
        }

        if (buttonB != null)
        {
            buttonB.onClick.RemoveListener(SelectB);
        }
    }


    // =========================================================
    // 色の切り替え
    // =========================================================

    /// <summary>
    /// 画像Aを選択時の色、画像Bを非選択時の色にし、
    /// 武器Aを使う武器として保存します。
    ///
    /// 使用例：
    /// imageColorSwitcher.SelectA();
    /// </summary>
    public void SelectA()
    {
        SetColor(imageA, imageASelectedColor);
        SetColor(imageB, imageBUnselectedColor);

        PlayerAttack.SaveSelectedWeapon(weaponA);
    }


    /// <summary>
    /// 画像Bを選択時の色、画像Aを非選択時の色にし、
    /// 武器Bを使う武器として保存します。
    ///
    /// 使用例：
    /// imageColorSwitcher.SelectB();
    /// </summary>
    public void SelectB()
    {
        SetColor(imageA, imageAUnselectedColor);
        SetColor(imageB, imageBSelectedColor);

        PlayerAttack.SaveSelectedWeapon(weaponB);
    }


    /// <summary>
    /// 画像が設定されている場合だけ色を変更します。
    /// </summary>
    private void SetColor(Image image, Color color)
    {
        if (image == null)
        {
            Debug.LogWarning("ImageColorSwitcher：画像が設定されていません。");
            return;
        }

        image.color = color;
    }
}
