리본 버튼 아이콘(PNG)을 여기에 둡니다. 없어도 버튼은 동작합니다(아이콘만 비어 보임).

기대 파일명 (32x32 PNG 권장):
  Settings32.png   - [정지 설정] 버튼
  Grade32.png      - [정지면 생성] 버튼

.csproj가 Resources\*.png 를 DLL에 EmbeddedResource로 포함합니다.
리소스 이름은 "DH.Grading.Civil.Resources.<파일명>" 으로 RibbonApp.LoadIcon이 찾습니다.
