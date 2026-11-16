import math, os
INK="#16181D"; ACC="#FF5A1F"
def rpoly(pts, rs):
    n=len(pts); d=""
    for i in range(n):
        p0=pts[i-1]; p1=pts[i]; p2=pts[(i+1)%n]; r=rs[i]
        v1=(p0[0]-p1[0],p0[1]-p1[1]); v2=(p2[0]-p1[0],p2[1]-p1[1])
        l1=math.hypot(*v1); l2=math.hypot(*v2)
        u1=(v1[0]/l1,v1[1]/l1); u2=(v2[0]/l2,v2[1]/l2)
        ang=math.acos(max(-1,min(1,u1[0]*u2[0]+u1[1]*u2[1])))
        t=r/math.tan(ang/2)
        a=(p1[0]+u1[0]*t,p1[1]+u1[1]*t); b=(p1[0]+u2[0]*t,p1[1]+u2[1]*t)
        sweep=1 if (u1[0]*u2[1]-u1[1]*u2[0])<0 else 0
        d+=("M" if i==0 else "L")+f"{a[0]:.2f} {a[1]:.2f}A{r} {r} 0 0 {sweep} {b[0]:.2f} {b[1]:.2f}"
    return d+"Z"
def pieces(lo,hi,g,slip,ro,rc):
    k=g*math.sqrt(2); sh=slip/math.sqrt(2)
    tl=[(x+sh,y-sh) for x,y in [(lo,lo),(hi-k,lo),(lo,hi-k)]]
    br=[(x-sh,y+sh) for x,y in [(hi,lo+k),(hi,hi),(lo+k,hi)]]
    return rpoly(tl,[ro,rc,rc]), rpoly(br,[rc,ro,rc])
# symbol (regular) and small-size cut
SYM=pieces(38,218,11,14,24,5)
SMALL=pieces(22,234,16,10,28,8)
# ---------- wordmark (constructed, w=16, baseline 184, x-height 104, cap 72)
W=16; BASE=184; XH=104; CAP=72
def rect(x0,y0,x1,y1): return f"M{x0} {y0}H{x1}V{y1}H{x0}Z"
def ring(cx,cy,R,r):  # full ring (evenodd hole)
    return (f"M{cx-R} {cy}A{R} {R} 0 1 1 {cx+R} {cy}A{R} {R} 0 1 1 {cx-R} {cy}Z"
            f"M{cx-r} {cy}A{r} {r} 0 1 0 {cx+r} {cy}A{r} {r} 0 1 0 {cx-r} {cy}Z")
def letter_P(x):
    R=32; cx=x+W+12
    return (f"M{x} {CAP}H{cx}A{R} {R} 0 0 1 {cx} {CAP+2*R}H{x+W}V{BASE}H{x}Z"
            f"M{x+W} {CAP+W}V{CAP+2*R-W}H{cx}A{R-W} {R-W} 0 0 0 {cx} {CAP+W}Z"), cx+R-x
def letter_D(x):
    R=(BASE-CAP)/2; cx=x+W+28
    return (f"M{x} {CAP}H{cx}A{R} {R} 0 0 1 {cx} {BASE}H{x}Z"
            f"M{x+W} {CAP+W}V{BASE-W}H{cx}A{R-W} {R-W} 0 0 0 {cx} {CAP+W}Z"), cx+R-x
def letter_I(x): return rect(x,CAP,x+W,BASE), W
def letter_a(x):
    R=(BASE-XH)/2; cy=XH+R
    return ring(x+R,cy,R,R-W)+rect(x+2*R-W,XH,x+2*R,BASE), 2*R
def arcpath(cx,cy,rx,ry,t0,t1,w):
    # elliptical band from angle t0 to t1 (degrees, y-down), outer rx/ry, inner rx-w/ry-w
    def P(a,Rx,Ry): a=math.radians(a); return (cx+Rx*math.cos(a), cy+Ry*math.sin(a))
    large=1 if abs(t1-t0)>180 else 0; sw=1 if t1>t0 else 0
    a0=P(t0,rx,ry); a1=P(t1,rx,ry); b1=P(t1,rx-w,ry-w); b0=P(t0,rx-w,ry-w)
    return (f"M{a0[0]:.2f} {a0[1]:.2f}A{rx} {ry} 0 {large} {sw} {a1[0]:.2f} {a1[1]:.2f}"
            f"L{b1[0]:.2f} {b1[1]:.2f}A{rx-w} {ry-w} 0 {large} {1-sw} {b0[0]:.2f} {b0[1]:.2f}Z")
def letter_s(x):
    ry=(BASE-XH+W)/4; rx=30; cx=x+rx
    top=arcpath(cx,XH+ry,rx,ry,-28,-270,W)       # from upper-right, ccw over top/left to bottom
    bot=arcpath(cx,BASE-ry,rx,ry,-90,152,W)      # from top, cw over right/bottom to lower-left
    return top+bot, 2*rx
def wordmark(x0):
    seq=[(letter_P,0),(letter_a,9),(letter_s,9),(letter_s,8),(letter_D,13),(letter_P,15),(letter_I,15)]
    x=x0; d=""
    for f,gap in seq:
        x+=gap; p,w=f(x); d+=p; x+=w
    return d, x
def svg(vb,body,title="PassDPI logo"):
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{vb}"><title>{title}</title>{body}</svg>\n'
def sym_body(s,c1=INK,c2=ACC,dx=0,dy=0,sc=1):
    tr=f' transform="translate({dx} {dy}) scale({sc})"' if (dx or dy or sc!=1) else ''
    return f'<g{tr}><path d="{s[0]}" fill="{c1}"/><path d="{s[1]}" fill="{c2}"/></g>'
os.makedirs("master",exist_ok=True)
w=lambda n,t: open(f"master/{n}",'w').write(t)
w("passdpi-symbol.svg", svg("0 0 256 256",sym_body(SYM)))
w("passdpi-symbol-small.svg", svg("0 0 256 256",sym_body(SMALL)))
w("passdpi-symbol-reversed.svg", svg("0 0 256 256",sym_body(SYM,"#FFFFFF",ACC)))
wd,end=wordmark(0)
w("passdpi-wordmark.svg", svg(f"0 {CAP-8} {end} {BASE-CAP+16}",f'<path d="{wd}" fill="{INK}" fill-rule="nonzero"/>'))
# horizontal lockup: symbol 256, gap 40, wordmark
hd,hend=wordmark(256+28)
w("passdpi-horizontal.svg", svg(f"0 0 {hend+40} 256", sym_body(SYM)+f'<path d="{hd}" fill="{INK}" fill-rule="nonzero"/>'))
w("passdpi-horizontal-reversed.svg", svg(f"0 0 {hend+40} 256", sym_body(SYM,"#FFFFFF",ACC)+f'<path d="{hd}" fill="#FFFFFF" fill-rule="nonzero"/>'))
# stacked: symbol centred above wordmark
sd,send=wordmark(0); Wd=max(send,256)+80
sym_x=(Wd-256)/2; wm_x=(Wd-send)/2
stack=sym_body(SYM,dx=sym_x)+f'<g transform="translate({wm_x} {256-CAP+20})"><path d="{sd}" fill="{INK}" fill-rule="nonzero"/></g>'
w("passdpi-stacked.svg", svg(f"0 0 {Wd} {256+BASE-CAP+48}",stack))
print("wordmark width",end)
