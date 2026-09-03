必要なクラス
ブロッククラス：[text](https://sites.google.com/site/letsmakeavoxelengine/home/block-data-structure?authuser=0)
ブロックを表示/非表示を定義するフラグ
ブロックのタイプ（道なのか、ビルなのかなどのどのブロックなのかの情報を持つ）

具体的なクラスなど
class Block {
  public: Block();
  ~Block();
  bool IsActive();
  void SetActive(bool active);
  private: bool m_active;
  BlockType m_blockType;
};
enum BlockType {
 BlockType_Default = 0,
 BlockType_Grass,
 BlockType_Dirt,
 BlockType_Water,
 BlockType_Stone,
 BlockType_Wood,
 BlockType_Sand,
 BlockType_NumTypes,
};