import wave,math,array,random
sr=24000
out=array.array('h')
rng=random.Random(84)
for i in range(24*sr):
 t=i/sr
 chord=[(50,57,65,69),(46,53,60,65),(53,60,64,67),(48,55,62,67)][int(t/6)]
 p=t%.625
 q=t%.3125
 freq=lambda m:440*2**((m-69)/12)
 pad=sum(math.sin(2*math.pi*freq(n)*t+.18*math.sin(t)) for n in chord)*.048*min(1,(t%6)*3,(6-t%6)*3)
 arp=.13*math.sin(2*math.pi*freq(chord[int(t/.3125)%4]+12)*q)*math.exp(-8*q)*min(1,q*140)
 kick=.21*math.sin(2*math.pi*(49*p+1.6*(1-math.exp(-28*p))))*math.exp(-16*p)
 hat=.075*rng.uniform(-1,1)*math.exp(-55*q)
 out.append(int(32767*(pad+arp+kick+hat)*min(1,t*1.6,(24-t)/1.8)))
with wave.open('public/score.wav','wb') as w:
 w.setparams((1,2,sr,0,'NONE','not compressed'));w.writeframes(out.tobytes())
