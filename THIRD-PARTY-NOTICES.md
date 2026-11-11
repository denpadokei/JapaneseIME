# サードパーティ ライセンス

## JMdict（かな漢字辞書データ）

`Resources/kanji-dictionary.txt.gz`（プラグインに埋め込み）は、JMdict から
読みと漢字表記のみを抽出して作成した派生データです。

- 原データ: JMdict / EDICT 辞書プロジェクト
  （Electronic Dictionary Research and Development Group: EDRDG）
- URL: https://www.edrdg.org/wiki/index.php/JMdict-EDICT_Dictionary_Project
- ライセンス: Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0)
  https://creativecommons.org/licenses/by-sa/4.0/

派生データ（`Resources/kanji-dictionary.txt.gz`）も CC BY-SA 4.0 で提供します。
再生成するには `Tools/build_dictionary.py` に JMdict のアーカイブを渡してください。

```
python Tools/build_dictionary.py JMdict_e.gz Resources/kanji-dictionary.txt.gz
```
