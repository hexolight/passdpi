import math
def rpoly(pts, r, rs=None):
    # polygon with rounded corners (arcs)
    n=len(pts); d=""
    for i in range(n):
        p0=pts[i-1]; p1=pts[i]; p2=pts[(i+1)%n]
        v1=(p0[0]-p1[0],p0[1]-p1[1]); v2=(p2[0]-p1[0],p2[1]-p1[1])
        l1=math.hypot(*v1); l2=math.hypot(*v2)
        u1=(v1[0]/l1,v1[1]/l1); u2=(v2[0]/l2,v2[1]/l2)
        rr=(rs[i] if rs else r)
        ang=math.acos(max(-1,min(1,u1[0]*u2[0]+u1[1]*u2[1])))
        t=rr/math.tan(ang/2)
        a=(p1[0]+u1[0]*t,p1[1]+u1[1]*t); b=(p1[0]+u2[0]*t,p1[1]+u2[1]*t)
        cross=u1[0]*u2[1]-u1[1]*u2[0]
        sweep=1 if cross<0 else 0
        d+=("M" if i==0 else "L")+f"{a[0]:.2f} {a[1]:.2f} A{rr} {rr} 0 0 {sweep} {b[0]:.2f} {b[1]:.2f} "
    return d+"Z"
W='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">'
# A: slipped packet
g=11; s=14  # half-gap, slip along diagonal
lo,hi=40,216
k=g*math.sqrt(2)
tl=[(lo,lo),(hi-k,lo),(lo,hi-k)]
br=[(hi,lo+k),(hi,hi),(lo+k,hi)]
sh=s/math.sqrt(2)
tl=[(x+sh,y-sh) for x,y in tl]; br=[(x-sh,y+sh) for x,y in br]
# recentre
open('a-symbol.svg','w').write(W+f'<path d="{rpoly(tl,22,[24,5,5])} {rpoly(br,22,[5,24,5])}" fill="#000"/></svg>')
# B: passage P
sx0,sx1=64,106; gap=20; bx=sx1+gap; top,bot=40,216; t=40; R=56
cx=192-R
cy=top+R
p=(f'M{sx0+12} {top} H{sx1-12} A12 12 0 0 1 {sx1} {top+12} V{bot-12} A12 12 0 0 1 {sx1-12} {bot} H{sx0+12} A12 12 0 0 1 {sx0} {bot-12} V{top+12} A12 12 0 0 1 {sx0+12} {top} Z '
   f'M{bx} {top} H{cx} A{R} {R} 0 0 1 {cx} {top+2*R} H{bx} V{top+2*R-t} H{cx} A{R-t} {R-t} 0 0 0 {cx} {top+t} H{bx} Z')
open('b-symbol.svg','w').write(W+f'<path d="{p}" fill="#000" fill-rule="evenodd"/></svg>')
# C: filter + packet
bw=28; gp=20; x0=34; y0=48; y1=208; sl0=114; sl1=142
d=""
for i in range(3):
    x=x0+i*(bw+gp)
    d+=f'M{x} {y0+8} A8 8 0 0 1 {x+8} {y0} H{x+bw-8} A8 8 0 0 1 {x+bw} {y0+8} V{sl0} H{x} Z '
    d+=f'M{x} {sl1} H{x+bw} V{y1-8} A8 8 0 0 1 {x+bw-8} {y1} H{x+8} A8 8 0 0 1 {x} {y1-8} Z '
cxp=x0+3*bw+2*gp+gp+4+22
d+=f'M{cxp-22} 128 A22 22 0 1 0 {cxp+22} 128 A22 22 0 1 0 {cxp-22} 128 Z'
open('c-symbol.svg','w').write(W+f'<path d="{d}" fill="#000"/></svg>')
for n in 'abc':
    sym=open(f'{n}-symbol.svg').read().split('>',1)[1].rsplit('</svg>',1)[0]
    open(f'{n}-lockup.svg','w').write('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 760 256">'+sym+
      '<text x="280" y="170" font-family="Segoe UI Variable Display, Segoe UI, sans-serif" font-weight="600" font-size="128" letter-spacing="-3" fill="#000">PassDPI</text></svg>')
