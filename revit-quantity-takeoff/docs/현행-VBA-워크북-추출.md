# 현행 VBA 워크북 추출

## 시트 목록
- **CSV_Import** : dims=A1:N313, max_row=313, max_col=14
- **Formula** : dims=A1:O31, max_row=31, max_col=15
- **Calc_Sheet** : dims=A1:C28, max_row=28, max_col=3
- **Quantity_Report** : dims=A1:Z223, max_row=223, max_col=26

## CSV_Import  (max_row=313, max_col=14)
  A1=DH_ElementCode | B1=DH_Class | C1=DH_Category | D1=L1 | E1=L2 | F1=L3 | G1=W1 | H1=W2 | I1=W3 | J1=H | K1=ETC | L1=DH_Zone | M1=DH_Part | N1=ElementID
  A2=C1 | B2=Body | C2=기둥 | D2=0.3 | E2=0 | F2=0 | G2=0.3 | H2=0 | I2=0 | J2=4.94999999999999 | K2=0 | L2=수조부 | M2=기둥 | N2=825923
  A3=C1 | B3=Body | C3=기둥 | D3=0.3 | E3=0 | F3=0 | G3=0.3 | H3=0 | I3=0 | J3=4.94999999999999 | K3=0 | L3=수조부 | M3=기둥 | N3=825924
  A4=C1 | B4=Body | C4=기둥 | D4=0.3 | E4=0 | F4=0 | G4=0.3 | H4=0 | I4=0 | J4=4.94999999999999 | K4=0 | L4=수조부 | M4=기둥 | N4=825925
  A5=C1 | B5=Body | C5=기둥 | D5=0.3 | E5=0 | F5=0 | G5=0.3 | H5=0 | I5=0 | J5=4.94999999999999 | K5=0 | L5=수조부 | M5=기둥 | N5=825926
  A6=C1 | B6=Body | C6=기둥 | D6=0.3 | E6=0 | F6=0 | G6=0.3 | H6=0 | I6=0 | J6=4.94999999999999 | K6=0 | L6=수조부 | M6=기둥 | N6=825927
  A7=C1 | B7=Body | C7=기둥 | D7=0.3 | E7=0 | F7=0 | G7=0.3 | H7=0 | I7=0 | J7=4.94999999999999 | K7=0 | L7=수조부 | M7=기둥 | N7=825928
  A8=C1 | B8=Body | C8=기둥 | D8=0.3 | E8=0 | F8=0 | G8=0.3 | H8=0 | I8=0 | J8=4.94999999999999 | K8=0 | L8=수조부 | M8=기둥 | N8=825929

## Formula  (max_row=31, max_col=15)
  A1=HostID | B1=무근콘크리트 | C1=철근콘크리트 | D1=거푸집(합판6회) | E1=거푸집(합판4회) | F1=거푸집(합판3회) | G1=유로폼 | H1=시스템 비계 | I1=강관조립 말비계(이동식) | J1=강관동바리 | K1=시스템 동바리 | L1=방수 | M1=콘크리트 표면마무리 | N1=콘크리트 양생 | O1=콘크리트치핑
  A2=L1 | B2=([L1]*[W1]+[L2]*[W2]+[L3]*[W3])*[H] | D2=([W1]+[L1_L_Long]+[W3])*[H]
  A3=L2 | B3=[L1]*[W1]*[H] | D3=([L1]*2+[W1])*[H]
  A4=L3 | B4=([W1]+[W2])*[H]*1/2*([L1]*2+[L2]*2+[L3]) | D4=([L1]*2+[L2]*2+[L3])*[H]
  A5=L4 | B5=[L1]*[W1]*[H] | D5=([W1]+[L1]*2)*[H]
  A6=B1 | C6=([L1]*[W1]+[L2]*[W2]+[L3]*[W3])*[H] | F6=([W1]+[B1_L_Long]+[W3])*[H]
  A7=B2 | C7=[L1]*[W1]*[H] | F7=([L1]*2+[W1])*[H]
  A8=B3 | C8=[L1]*[W1]*[H]
  A9=B4 | C9=[L1]*[W1]*[H] | F9=([L1]*2+[W1])*[H]
  A10=S1 | C10=[L1]*[W1]*[H] | E10=[S1_W_Form]*[S1_L_Form]
  A11=S2 | C11=[L1]*[W1]*[H] | E11=[S2_BW_Form]*[S2_BL_Form]+[S2_EW_Form]*[S2_EL_Form]
  A12=MS1 | C12=[L1]*[W1]*[H] | E12=[MS1_W_Form]*[MS1_L_Form]
  A13=TC1 | B13=[L1]*[W1]*[H] | D13=([W1]*2+[L1]*2)*[H1]
  A14=TC2 | B14=[L1]*[W1]*[H] | D14=([W1]*2+[L1]*2)*[H1]
  A15=TC3 | B15=[L1]*[W1]*[H] | D15=([W1]*2+[L1]*2)*[H1]
  A16=C1 | C16=[L1]^2*[C1_Up]+([C1_Bottom]^2+[L1]^2)*1/2*[ETC] | F16=([L1]+[C1_Bottom])*1/2*[C1_H_Form]*4+[L1]*[C1_Up]*4
  A17=G1 | C17=[L1]*[W1]*[H] | F17=[L1]*[H]*2
  A18=G2 | C18=[L1]*[W1]*[H] | F18=[L1]*[H]*2
  A19=W1 | C19=[L1]*[H]*[W1] | F19=(내측)#[L1]*[H]|(외측)#[L1]*[H]
  A20=W2 | C20=[L1]*[H]*[W1]
  A21=W3 | C21=[L1]*[H]*[W1]
  A22=W4 | C22=[L1]*[H]*[W1]
  A23=W5 | C23=[L1]*[H]*[W1]
  A24=W5 | C24=[L1]*[H]*[W1]
  A25=W6 | C25=[L1]*[H]*[W1]
  A26=W6 | C26=[L1]*[H]*[W1]
  A27=W7 | C27=[L1]*[H]*[W1]
  A28=W8 | C28=[L1]*[H]*[W1]
  A29=W9 | C29=[L1]*[H]*[W1]
  A30=W10 | C30=[L1]*[H]*[W1]
  A31=L2_Sub | B31=-([L1]*[H]*[W1])

## Calc_Sheet  (max_row=28, max_col=3)
  A1=Calc_Code | B1=Formula | C1=Description
  A2=L1_L_Long | B2==VLOOKUP("L1",CSV_Import!$A:$K,4,FALSE)+VLOOKUP("L1",CSV_Import!$A:$K,5,FALSE)+VLOOKUP("L1",CSV_Import!$A:$K,6,FALSE)  ⟶[30.5999999999999] | C2=L1 수조부 버림 세로 총길이
  A3=L1_W_Long | B3==VLOOKUP("L1",CSV_Import!$A:$K,7,FALSE)*2  ⟶[50.8] | C3=L1 수조부 버림 가로 총길이
  A4=B1_L_Long | B4==VLOOKUP("B1",CSV_Import!$A:$K,4,FALSE)+VLOOKUP("B1",CSV_Import!$A:$K,5,FALSE)+VLOOKUP("B1",CSV_Import!$A:$K,6,FALSE)  ⟶[30.39999999999989] | C4=B1 수조부 기초 세로 총길이
  A5=B1_W_Long | B5==VLOOKUP("B1",CSV_Import!$A:$K,7,FALSE)*2  ⟶[50.6] | C5=B1 수조부 기초 가로 총길이
  A6=S1_L_Inner | B6==VLOOKUP("S1",CSV_Import!$A:$K,4,FALSE)-VLOOKUP("W3",CSV_Import!$A:$K,7,FALSE)-VLOOKUP("W4",CSV_Import!$A:$K,7,FALSE)  ⟶[29.9999999999999] | C6=S1 수조부 내부 세로길이
  A7=S1_W_Inner | B7==VLOOKUP("S1",CSV_Import!$A:$K,7,FALSE)-VLOOKUP("W1",CSV_Import!$A:$K,7,FALSE)-VLOOKUP("W6",CSV_Import!$A:$K,7,FALSE)/2  ⟶[24.9999999999999] | C7=S1 수조부 내부 가로길이
  A8=S1_L_Form | B8==$B$6-$B$20*2  ⟶[28.9999999999999] | C8=S1 수조부 내부 거푸집세로길이
  A9=S1_W_Form | B9==$B$7-$B$20*2  ⟶[23.9999999999999] | C9=S1 수조부 내부 거푸집가로길이
  A10=S2_BL_Inner | B10==VLOOKUP("S2",CSV_Import!$A:$K,4,FALSE)-VLOOKUP("W9",CSV_Import!$A:$K,7,FALSE)  ⟶[4.99999999999999] | C10=S2 배관실 내부 세로길이
  A11=S2_BW_Inner | B11==VLOOKUP("S2",CSV_Import!$A:$K,7,FALSE)-VLOOKUP("W7",CSV_Import!$A:$K,7,FALSE)-VLOOKUP("W10",CSV_Import!$A:$K,7,FALSE)-VLOOKUP("W8",CSV_Import!$A:$K,7,FALSE)-VLOOKUP("W10",CSV_Import!$A:$K,11,FALSE)  ⟶[27.299999999999912] | C11=S2 배관실 내부 가로길이
  A12=S2_BL_Form | B12==$B$10-$B$22*2  ⟶[3.9999999999999902] | C12=S2 배관실 내부 거푸집세로길이
  A13=S2_BW_Form | B13==$B$11-$B$22*2  ⟶[26.299999999999912] | C13=S2 배관실 내부 거푸집가로길이
  A14=S2_EL_Inner | B14==VLOOKUP("S2",CSV_Import!$A:$K,4,FALSE)-VLOOKUP("W9",CSV_Import!$A:$K,7,FALSE)  ⟶[4.99999999999999] | C14=S2 전기실 내부 세로길이
  A15=S2_EW_Inner | B15==VLOOKUP("W10",CSV_Import!$A:$K,11,FALSE)  ⟶[2.49999999999999] | C15=S2 전기실 내부 가로길이
  A16=S2_EL_Form | B16==$B$14-$B$22*2  ⟶[3.9999999999999902] | C16=S2 전기실 내부 거푸집세로길이
  A17=S2_EW_Form | B17==$B$15-$B$22*2  ⟶[1.4999999999999898] | C17=S2 전기실 내부 거푸집가로길이
  A18=MS1_L_Form | B18==VLOOKUP("MS1",CSV_Import!$A:$K,4,FALSE)-$B$24*2  ⟶[3.9999999999999902] | C18=MS1 배관실 중간 내부 세로길이
  A19=MS1_W_Form | B19==VLOOKUP("MS1",CSV_Import!$A:$K,7,FALSE)-$B$24*2  ⟶[28.9999999999999] | C19=MS1 배관실 중간 내부 가로길이
  A20=H1_haunch | B20==IFERROR(VLOOKUP("H1",CSV_Import!$A:$K,7,FALSE),0)  ⟶[0.5] | C20=H1 수조부 헌치
  A21=H1_Form | B21==ROUND(SQRT($B$20^2+$B$20^2),2)  ⟶[0.71] | C21=H1 수조부 빗변길이
  A22=H2_haunch | B22==IFERROR(VLOOKUP("H2",CSV_Import!$A:$K,7,FALSE),0)  ⟶[0.5] | C22=H2 배관실 2층 헌치
  A23=H2_Form | B23==ROUND(SQRT($B$22^2+$B$22^2),2)  ⟶[0.71] | C23=H2 수조부 빗변길이
  A24=H3_haunch | B24==IFERROR(VLOOKUP("H3",CSV_Import!$A:$K,7,FALSE),0)  ⟶[0.5] | C24=H2 배관실 1층 헌치
  A25=H3_Form | B25==ROUND(SQRT($B$24^2+$B$24^2),2)  ⟶[0.71] | C25=H3 수조부 빗변길이
  A26=C1_Up | B26==IFERROR(VLOOKUP("C1",CSV_Import!$A:$K,10,FALSE)-VLOOKUP("C1",CSV_Import!$A:$K,11,FALSE),VLOOKUP("C1",CSV_Import!$A:$K,10,FALSE))  ⟶[4.94999999999999] | C26=C1 기둥부 세로 길이
  A27=C1_Bottom | B27==VLOOKUP("C1",CSV_Import!$A:$K,11,FALSE)*2+VLOOKUP("C1",CSV_Import!$A:$K,4,FALSE)  ⟶[0.3] | C27=C1 기둥헌치부 밑변 길이
  A28=C1_H_Form | B28==ROUND(SQRT(VLOOKUP("C1",CSV_Import!$A:$K,11,FALSE)^2+VLOOKUP("C1",CSV_Import!$A:$K,11,FALSE)^2),2)  ⟶[0] | C28=C1 기둥헌치 빗변길이

## Quantity_Report  (max_row=223, max_col=26)
  A1=  | B1=산         출           근         거  | Z1=수  량
  A2=무근콘크리트
  B3=ㅇ 슬래브
  E4=L1 : | F4=( | G4=( | H4=27.6 | J4=× | K4=25.4 | M4=+ | N4=2.7 | P4=× | Q4=22.6 | S4=+ | T4=0.3
  F6=× | G6=10.2 | I6=) | J6=× | K6=0.1 | M6=× | N6=2 | P6=) | V6== | W6==ROUND(((H4*K4+N4*Q4+T4*G6)*K6*N6), 2)  ⟶[153.02]
  E8=L2 : | F8=2.7 | H8=× | I8=5.6 | K8=× | L8=0.1 | V8== | W8==ROUND(F8*I8*L8, 2)  ⟶[1.51]
  E10=L3 : | F10=( | G10=2.1 | I10=+ | J10=0.1 | L10=) | M10=× | N10=2 | P10=× | Q10=1 | S10=/ | T10=2
  F12=× | G12=( | H12=12.4 | J12=× | K12=2 | M12=+ | N12=2.7 | P12=× | Q12=2 | S12=+ | T12=5.6
  F14=) | V14== | W14==ROUND((G10+J10)*N10*Q10/T10*(H12*K12+N12*Q12+T12), 2)  ⟶[78.76]
  E16=L4 : | F16=5.5 | H16=× | I16=30.6 | K16=× | L16=0.1 | V16== | W16==ROUND(F16*I16*L16, 2)  ⟶[16.83]
  E18=TC1 : | F18=4.8 | H18=× | I18=29.8 | K18=× | L18=0.1 | V18== | W18==ROUND(F18*I18*L18, 2)  ⟶[14.3]
  E20=TC2 : | F20=4.8 | H20=× | I20=27.1 | K20=× | L20=0.1 | V20== | W20==ROUND(F20*I20*L20, 2)  ⟶[13.01]
  E22=TC3 : | F22=4.8 | H22=× | I22=2.3 | K22=× | L22=0.1 | V22== | W22==ROUND(F22*I22*L22, 2)  ⟶[1.1]
  Y25=계 : | Z25==SUM(W6,W8,W14,W16,W18,W20,W22)  ⟶[278.53000000000003]
  A28=철근콘크리트
  B29=ㅇ 기둥
  E30=C1 : | F30=( | G30=0.3 | I30=2 | J30=× | K30=4.95 | M30=+ | N30=( | O30=0.3 | Q30=2 | R30=+ | S30=0.3 | U30=2
  F32=) | G32=× | H32=1 | J32=/ | K32=2 | M32=× | N32=0 | P32=× | Q32=60 | S32=) | V32== | W32==ROUND((G30^2*K30+(O30^2+S30^2)*H32/K32*N32*Q32), 2)  ⟶[0.45]
  B35=ㅇ 벽체
  E36=B3 : | F36=( | G36=12.6 | I36=× | J36=0.2 | L36=× | M36=1.7 | O36=× | P36=2 | R36=) | S36=+
  F38=( | G38=2.5 | I38=× | J38=0.2 | L38=× | M38=1.7 | O38=× | P38=2 | R38=) | S38=+ | T38=5.6
  F40=× | G40=0.2 | I40=× | J40=1.7 | V40== | W40==ROUND((G36*J36*M36*P36)+(G38*J38*M38*P38)+T38*G40*J40, 2)  ⟶[12.17]
